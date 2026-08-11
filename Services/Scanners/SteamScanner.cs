using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Steam library folders for installed games.
    /// </summary>
    public class SteamScanner : IPlatformScanner
    {
        private readonly List<string> _libraryPaths;

        public string PlatformName => "Steam";

        public SteamScanner(List<string> libraryPaths)
        {
            // Wenn keine Pfade konfiguriert sind, automatisch erkennen
            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                _libraryPaths = GetAutoDetectedPaths();
                if (_libraryPaths.Count > 0)
                    Logger.Log($"Steam auto-detect: found {_libraryPaths.Count} path(s).");
                else
                    Logger.Log("Steam auto-detect: no Steam installation found.");
            }
            else
            {
                _libraryPaths = libraryPaths;
            }
        }

        /// <summary>
        /// Versucht Steam-Bibliothekspfade automatisch zu erkennen:
        /// 1. Windows-Registry (HKLM\SOFTWARE\WOW6432Node\Valve\Steam)
        /// 2. Bekannte Standard-Installationspfade als Fallback
        /// </summary>
        public static List<string> GetAutoDetectedPaths()
        {
            var found = new List<string>();

            // 1. Registry-Pfad
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Valve\Steam");
                string? installPath = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    ScannerPathUtility.AddExistingDirectory(found, Path.Combine(installPath, "steamapps"));

                    // libraryfolders.vdf enthält weitere Bibliotheken
                    string vdfPath = Path.Combine(installPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdfPath))
                    {
                        foreach (var line in File.ReadAllLines(vdfPath))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(
                                line, "\"path\"\\s+\"([^\"]+)\"");
                            if (match.Success)
                            {
                                string extraPath = match.Groups[1].Value.Replace("\\\\", "\\");
                                ScannerPathUtility.AddExistingDirectory(found, Path.Combine(extraPath, "steamapps"));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Steam registry detection failed", ex);
            }

            // 2. Fallback: bekannte Standard-Pfade
            if (found.Count == 0)
            {
                var fallbacks = new[]
                {
                    @"C:\Program Files (x86)\Steam\steamapps",
                    @"C:\Program Files\Steam\steamapps",
                };
                foreach (var f in fallbacks)
                    ScannerPathUtility.AddExistingDirectory(found, f);
            }

            return found;
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();
            string? steamRoot = null;

            // Try to find Steam root from library paths
            foreach (var path in _libraryPaths)
            {
                if (!ScannerPathUtility.TryNormalize(path, out var normalizedPath))
                {
                    continue;
                }

                if (normalizedPath.EndsWith("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(normalizedPath);
                    if (parent != null && File.Exists(Path.Combine(parent.FullName, "steam.exe")))
                    {
                        steamRoot = parent.FullName;
                        break;
                    }
                }
            }

            foreach (var libPath in _libraryPaths)
            {
                if (!Directory.Exists(libPath))
                {
                    Logger.Log($"Skipping missing library path: {libPath}");
                    continue;
                }

                Logger.Log($"Scanning Steam library: {libPath}");

                try
                {
                    var acfFiles = Directory.GetFiles(libPath, "appmanifest_*.acf");
                    foreach (var file in acfFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            string content = File.ReadAllText(file);

                            string? name = TryReadManifestValue(content, "name");
                            string? appid = TryReadManifestValue(content, "appid");
                            string? installDir = TryReadManifestValue(content, "installdir");

                            if (!string.IsNullOrWhiteSpace(name) &&
                                !string.IsNullOrWhiteSpace(appid) &&
                                appid.All(char.IsAsciiDigit))
                            {
                                string imageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg";

                                // Check local cache
                                if (!string.IsNullOrEmpty(steamRoot))
                                {
                                    string localCache = Path.Combine(steamRoot, "appcache", "librarycache", $"{appid}_header.jpg");
                                    if (File.Exists(localCache))
                                    {
                                        imageUrl = localCache;
                                    }
                                }

                                string fullInstallPath = "";
                                if (!string.IsNullOrEmpty(installDir))
                                {
                                    fullInstallPath = Path.Combine(libPath, "common", installDir);
                                }

                                games.Add(new Game
                                {
                                    Id = $"steam:{appid}",
                                    Name = name,
                                    Platform = "Steam",
                                    Source = "Steam Library",
                                    Path = $"steam://rungameid/{appid}",
                                    LaunchType = "uri",
                                    IsManual = false,
                                    ImageUrl = imageUrl,
                                    InstallDirectory = fullInstallPath
                                });
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error parsing Steam manifest {file}", ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning Steam lib {libPath}", ex);
                }
            }

            return games;
        }

        internal static string? TryReadManifestValue(string content, string key)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var match = Regex.Match(
                content,
                $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
                RegexOptions.CultureInvariant);
            return match.Success ? UnescapeManifestValue(match.Groups[1].Value) : null;
        }

        private static string UnescapeManifestValue(string value)
        {
            var result = new StringBuilder(value.Length);
            bool escaped = false;
            foreach (char character in value)
            {
                if (escaped)
                {
                    result.Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else
                {
                    result.Append(character);
                }
            }

            if (escaped)
            {
                result.Append('\\');
            }

            return result.ToString();
        }
    }
}
