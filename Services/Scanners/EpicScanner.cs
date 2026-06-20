using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Epic Games library for installed games.
    /// </summary>
    public class EpicScanner : IPlatformScanner
    {
        private readonly List<string> _libraryPaths;

        public string PlatformName => "Epic Games";

        public EpicScanner(List<string> libraryPaths)
        {
            // Wenn keine Pfade konfiguriert sind, automatisch erkennen
            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                _libraryPaths = GetAutoDetectedPaths();
                if (_libraryPaths.Count > 0)
                    Logger.Log($"Epic auto-detect: found {_libraryPaths.Count} path(s).");
                else
                    Logger.Log("Epic auto-detect: no Epic installation found.");
            }
            else
            {
                _libraryPaths = libraryPaths;
            }
        }

        /// <summary>
        /// Versucht den Epic-Manifest-Pfad automatisch zu erkennen:
        /// 1. Windows-Registry (HKLM\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher)
        /// 2. Bekannter Standard-ProgramData-Pfad als Fallback
        /// </summary>
        public static List<string> GetAutoDetectedPaths()
        {
            var found = new List<string>();

            // 1. Registry
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher");
                string? appDataPath = key?.GetValue("AppDataPath") as string;
                if (!string.IsNullOrEmpty(appDataPath))
                {
                    string manifestPath = Path.Combine(appDataPath, "Manifests");
                    AddIfExists(found, manifestPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Epic registry detection failed", ex);
            }

            // 2. Fallback: Standard-ProgramData-Pfad
            if (found.Count == 0)
            {
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");
                AddIfExists(found, fallback);
            }

            return found;
        }

        private static void AddIfExists(List<string> list, string path)
        {
            if (Directory.Exists(path) && !list.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                list.Add(path);
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            foreach (var libPath in _libraryPaths)
            {
                if (!Directory.Exists(libPath))
                {
                    Logger.Log($"Skipping missing Epic path: {libPath}");
                    continue;
                }

                Logger.Log($"Scanning Epic library: {libPath}");

                try
                {
                    var itemFiles = Directory.GetFiles(libPath, "*.item");
                    foreach (var file in itemFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            string json = File.ReadAllText(file);
                            using (JsonDocument doc = JsonDocument.Parse(json))
                            {
                                var root = doc.RootElement;

                                string? displayName = null;
                                string? appName = null;

                                if (root.TryGetProperty("DisplayName", out var nameProp)) displayName = nameProp.GetString();
                                if (root.TryGetProperty("MainGameAppName", out var appProp)) appName = appProp.GetString();
                                if (string.IsNullOrEmpty(appName) && root.TryGetProperty("AppName", out var appNameProp)) appName = appNameProp.GetString();

                                if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(appName))
                                {
                                    string imageUrl = "";
                                    string? installLoc = null;
                                    string? launchExe = null;

                                    if (root.TryGetProperty("InstallLocation", out var locProp)) installLoc = locProp.GetString();
                                    if (root.TryGetProperty("LaunchExecutable", out var exeProp)) launchExe = exeProp.GetString();

                                    if (!string.IsNullOrEmpty(installLoc) && !string.IsNullOrEmpty(launchExe))
                                    {
                                        string fullExePath = Path.Combine(installLoc, launchExe);
                                        imageUrl = IconExtractor.GetIconFromExe(fullExePath, $"epic_{appName}");
                                    }

                                    games.Add(new Game
                                    {
                                        Id = $"epic:{appName}",
                                        Name = displayName,
                                        Platform = "Epic Games",
                                        Source = "Epic Library",
                                        Path = $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true",
                                        LaunchType = "uri",
                                        IsManual = false,
                                        ImageUrl = imageUrl,
                                        InstallDirectory = installLoc ?? ""
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error parsing Epic manifest {file}", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning Epic dir {libPath}", ex);
                }
            }

            return games;
        }
    }
}
