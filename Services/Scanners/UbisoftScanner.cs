using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;
using Microsoft.Win32;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Ubisoft Connect registry for installed games.
    /// </summary>
    public class UbisoftScanner : IPlatformScanner
    {
        public string PlatformName => "Ubisoft Connect";

        public static List<string> GetAutoDetectedPaths()
        {
            var paths = new List<string>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
                if (key == null)
                {
                    return paths;
                }

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var gameKey = key.OpenSubKey(subKeyName);
                    string? installDirectory = gameKey?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(installDirectory))
                    {
                        ScannerPathUtility.AddExistingDirectory(paths, installDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Ubisoft path detection failed", ex);
            }

            return ScannerPathUtility.GetLibraryDirectories(paths);
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            try
            {
                // Ubisoft Connect speichert die Installationen oft in der Registry
                string registryPath = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
                using (var key = Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key == null)
                    {
                        Logger.Log("Ubisoft Connect registry key not found.");
                        return games;
                    }

                    foreach (var gameIdStr in key.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using (var gameKey = key.OpenSubKey(gameIdStr))
                            {
                                if (gameKey == null) continue;

                                string? installDir = gameKey.GetValue("InstallDir") as string;
                                if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                                    continue;

                                // Versuche den Namen aus dem Ordnernamen abzuleiten
                                string gameName = new DirectoryInfo(installDir).Name;
                                if (string.IsNullOrEmpty(gameName)) continue;

                                string exePath = ExecutableSelector.FindPrimaryExecutable(
                                    installDir,
                                    "unins", "crash", "redist");
                                string iconUrl = "";

                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    iconUrl = IconExtractor.GetIconFromExe(exePath, $"ubi_{gameIdStr}");
                                }

                                var game = new Game
                                {
                                    Id = $"ubi_{gameIdStr}",
                                    Name = gameName,
                                    Path = $"uplay://launch/{gameIdStr}/0",
                                    Args = "",
                                    Platform = "Ubisoft Connect",
                                    LaunchType = "uri",
                                    ImageUrl = iconUrl,
                                    InstallDirectory = installDir
                                };

                                games.Add(game);
                                Logger.Log($"Found Ubisoft game: {gameName} (ID: {gameIdStr})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error scanning Ubisoft game ID {gameIdStr}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error scanning Ubisoft games", ex);
            }

            return games;
        }

    }
}
