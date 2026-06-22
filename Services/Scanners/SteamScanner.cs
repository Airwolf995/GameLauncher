using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                if (path.EndsWith("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(path);
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

                            // Simple regex extraction
                            var nameMatch = Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"");
                            var appidMatch = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"");
                            var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"");

                            if (nameMatch.Success && appidMatch.Success)
                            {
                                string name = nameMatch.Groups[1].Value;
                                string appid = appidMatch.Groups[1].Value;
                                string installDir = installDirMatch.Success ? installDirMatch.Groups[1].Value : "";

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
                        catch (Exception ex)
                        {
                            Logger.Error($"Error parsing Steam manifest {file}", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning Steam lib {libPath}", ex);
                }
            }

            return games;
        }
    }
}
