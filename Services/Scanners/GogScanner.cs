using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans GOG Galaxy registry for installed games.
    /// </summary>
    public class GogScanner : IPlatformScanner
    {
        public string PlatformName => "GOG";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
                {
                    if (key == null)
                    {
                        Logger.Log("GOG registry key not found.");
                        return games;
                    }

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using (var gameKey = key.OpenSubKey(subKeyName))
                            {
                                if (gameKey == null) continue;

                                string? gameName = gameKey.GetValue("gameName") as string;
                                string? exePath = gameKey.GetValue("exe") as string;
                                string? workingDir = gameKey.GetValue("workingDir") as string;

                                if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(exePath))
                                    continue;

                                // Expand environment variables
                                exePath = Environment.ExpandEnvironmentVariables(exePath);

                                if (!File.Exists(exePath))
                                {
                                    Logger.Log($"GOG game exe not found: {exePath}");
                                    continue;
                                }

                                var game = new Game
                                {
                                    Id = $"gog_{subKeyName}",
                                    Name = gameName,
                                    Path = exePath,
                                    Args = "",
                                    Platform = "GOG",
                                    LaunchType = "exe",
                                    ImageUrl = IconExtractor.GetIconFromExe(exePath, $"gog_{subKeyName}"),
                                    InstallDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
                                };

                                games.Add(game);
                                Logger.Log($"Found GOG game: {gameName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error scanning GOG game {subKeyName}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error scanning GOG games", ex);
            }

            return games;
        }
    }
}
