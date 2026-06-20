using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
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

        public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{_githubRepo}/releases/latest";
                var response = await _httpClient.GetStringAsync(apiUrl, ct);
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string latestVersion = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
                string downloadUrl = "";
                string changelog = root.GetProperty("body").GetString() ?? "";

                // Find the installer asset
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                string currentVersion = GetCurrentVersion();
                Logger.Log($"Update Check: Current v{currentVersion} | Latest v{latestVersion}");

                if (IsNewerVersion(latestVersion, currentVersion) && !string.IsNullOrEmpty(downloadUrl))
                {
                    return new UpdateInfo
                    {
                        Version = latestVersion,
                        DownloadUrl = downloadUrl,
                        Changelog = changelog
                    };
                }

                return null; // No update available
            }
            catch (Exception ex)
            {
                Logger.Error("Update check failed", ex);
                return null;
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

        public async Task<bool> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken ct = default)
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

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Update download failed", ex);
                if (File.Exists(UpdaterExe)) try { File.Delete(UpdaterExe); } catch { }
                return false;
            }
        }


        public void InstallUpdate()
        {
            try
            {
                if (File.Exists(UpdaterExe))
                {
                    // Start installer with silent flag and auto-close option
                    var psi = new ProcessStartInfo
                    {
                        FileName = UpdaterExe,
                        Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                    
                    // Close the current application
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Update installation failed", ex);
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Changelog { get; set; } = "";
    }
}
