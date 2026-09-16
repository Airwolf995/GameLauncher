using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    public class UpdateService : IDisposable
    {
        private readonly string _githubRepo; // Format: "username/repo"
        private readonly HttpClient _httpClient;
        private static readonly string UpdaterExe = Path.Combine(Path.GetTempPath(), "GameLauncher_Setup.exe");

        public UpdateService(string githubRepo)
        {
            _githubRepo = githubRepo;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GameLauncher");
        }

        public string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{_githubRepo}/releases/latest";
                var response = await _httpClient.GetStringAsync(apiUrl, ct);
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string latestVersion = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
                string downloadUrl = "";
                string sha256 = "";
                string changelog = root.GetProperty("body").GetString() ?? "";

                // Find the installer asset
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (IsInstallerAssetName(name))
                        {
                            string assetUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            if (!IsTrustedDownloadUrl(assetUrl))
                            {
                                Logger.Error($"Update asset '{name}' has an unexpected download address and is ignored: {assetUrl}");
                                continue;
                            }

                            downloadUrl = assetUrl;
                            sha256 = ReadAssetSha256(asset);
                            break;
                        }
                    }
                }

                string currentVersion = GetCurrentVersion();
                Logger.Log($"Update Check: Current v{currentVersion} | Latest v{latestVersion}");

                if (IsNewerVersion(latestVersion, currentVersion) && !string.IsNullOrEmpty(downloadUrl))
                {
                    return UpdateCheckResult.UpdateAvailable(new UpdateInfo
                    {
                        Version = latestVersion,
                        DownloadUrl = downloadUrl,
                        Changelog = changelog,
                        Sha256 = sha256
                    });
                }

                return UpdateCheckResult.NoUpdateAvailable();
            }
            catch (Exception ex)
            {
                Logger.Error("Update check failed", ex);
                return UpdateCheckResult.Failed(ex);
            }
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (Version.TryParse(latest, out var latestVer) &&
                Version.TryParse(current, out var currentVer))
            {
                return latestVer > currentVer;
            }
            return false;
        }

        private static bool IsInstallerAssetName(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                return false;
            }

            return assetName.StartsWith("GameLauncher_Setup", StringComparison.OrdinalIgnoreCase) &&
                   assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static string ReadAssetSha256(JsonElement asset)
        {
            // GitHub meldet den Wert als "sha256:<hex>"; aeltere Releases kennen
            // das Feld noch nicht.
            if (!asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String)
            {
                return "";
            }

            string value = digest.GetString() ?? "";
            const string prefix = "sha256:";

            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length)
                : "";
        }

        internal static bool IsTrustedDownloadUrl(string downloadUrl)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // GitHub serves release assets from github.com and redirects to
            // objects.githubusercontent.com; anything else is unexpected.
            return IsHostOrSubdomainOf(uri.Host, "github.com") ||
                   IsHostOrSubdomainOf(uri.Host, "githubusercontent.com");
        }

        private static bool IsHostOrSubdomainOf(string host, string domain)
        {
            return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<UpdateDownloadResult> DownloadUpdateAsync(string downloadUrl, string expectedSha256, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                // Delete old installer if exists
                if (File.Exists(UpdaterExe))
                {
                    File.Delete(UpdaterExe);
                }

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var bytesRead = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(UpdaterExe, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    bytesRead += read;

                    if (totalBytes > 0 && progress != null)
                    {
                        var percentComplete = (int)((bytesRead * 100) / totalBytes);
                        progress.Report(percentComplete);
                    }
                }

                fileStream.Close(); // Close stream

                if (totalBytes > 0 && bytesRead != totalBytes)
                {
                    Logger.Error($"Update download is incomplete: {bytesRead} of {totalBytes} bytes");
                    TryDeleteDownloadedInstaller();
                    return UpdateDownloadResult.Failed;
                }

                if (!await VerifyDownloadedInstallerAsync(expectedSha256, ct))
                {
                    TryDeleteDownloadedInstaller();
                    return UpdateDownloadResult.ChecksumMismatch;
                }

                return UpdateDownloadResult.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("Update download failed", ex);
                TryDeleteDownloadedInstaller();
                return UpdateDownloadResult.Failed;
            }
        }

        private static async Task<bool> VerifyDownloadedInstallerAsync(string expectedSha256, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                // Aeltere Releases melden keinen Hash; dann bleibt es beim
                // bisherigen Verhalten.
                Logger.Log("Update download: no checksum published, skipping verification");
                return true;
            }

            using var stream = new FileStream(UpdaterExe, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            byte[] hash = await SHA256.HashDataAsync(stream, ct);
            string actual = Convert.ToHexString(hash);

            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error($"Update download does not match the published checksum. Expected {expectedSha256}, got {actual}");
                return false;
            }

            return true;
        }

        private static void TryDeleteDownloadedInstaller()
        {
            try
            {
                if (File.Exists(UpdaterExe))
                {
                    File.Delete(UpdaterExe);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Downloaded installer could not be deleted", ex);
            }
        }


        /// <summary>
        /// Startet den heruntergeladenen Installer. Gibt zurueck, ob er
        /// angelaufen ist; das Beenden der Anwendung obliegt dem Aufrufer, da
        /// der Installer sie sonst nicht ersetzen kann.
        /// </summary>
        public bool InstallUpdate()
        {
            try
            {
                if (!File.Exists(UpdaterExe))
                {
                    Logger.Error($"Update installation failed: the downloaded installer is missing at {UpdaterExe}");
                    return false;
                }

                // Start installer with silent flag and auto-close option
                var psi = new ProcessStartInfo
                {
                    FileName = UpdaterExe,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                };

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Update installation failed", ex);
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    public enum UpdateDownloadResult
    {
        Succeeded,

        /// <summary>Abbruch, Netzwerk- oder Dateifehler, unvollstaendige Datei.</summary>
        Failed,

        /// <summary>Die Datei kam vollstaendig an, passt aber nicht zur veroeffentlichten Pruefsumme.</summary>
        ChecksumMismatch
    }

    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Changelog { get; set; } = "";

        /// <summary>
        /// SHA-256 des Installers, wie ihn GitHub zum Asset meldet. Leer bei
        /// aelteren Releases, die noch ohne diese Angabe veroeffentlicht wurden.
        /// </summary>
        public string Sha256 { get; set; } = "";
    }

    public sealed class UpdateCheckResult
    {
        private UpdateCheckResult(bool succeeded, UpdateInfo? updateInfo, Exception? error)
        {
            Succeeded = succeeded;
            UpdateInfo = updateInfo;
            Error = error;
        }

        public bool Succeeded { get; }
        public UpdateInfo? UpdateInfo { get; }
        public Exception? Error { get; }
        public bool IsUpdateAvailable => UpdateInfo != null;

        public static UpdateCheckResult UpdateAvailable(UpdateInfo updateInfo) =>
            new(true, updateInfo, null);

        public static UpdateCheckResult NoUpdateAvailable() =>
            new(true, null, null);

        public static UpdateCheckResult Failed(Exception error) =>
            new(false, null, error);
    }
}
