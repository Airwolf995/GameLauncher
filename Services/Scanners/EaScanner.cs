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
    /// Scans EA App (formerly Origin) for installed games.
    /// </summary>
    public class EaScanner : IPlatformScanner
    {
        public string PlatformName => "EA App";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            try
            {
                // EA App speichert Spiele in der Uninstall Registry
                string uninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
                using (var key = Registry.LocalMachine.OpenSubKey(uninstallPath))
                {
                    if (key == null)
                    {
                        Logger.Log("EA uninstall registry key not found.");
                        return games;
                    }

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using (var appKey = key.OpenSubKey(subKeyName))
                            {
                                if (appKey == null) continue;

                                string? publisher = appKey.GetValue("Publisher") as string;
                                string? installLocation = appKey.GetValue("InstallLocation") as string;
                                string? displayName = appKey.GetValue("DisplayName") as string;

                                // Filter für Electronic Arts
                                if (string.IsNullOrEmpty(publisher) || !publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                                    continue;

                                // Ignoriere den EA-Launcher selbst
                                if (displayName != null && (displayName.Contains("EA app") || displayName.Contains("Origin")))
                                    continue;

                                string exePath = FindPrimaryExe(installLocation);
                                string iconUrl = "";

                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    iconUrl = IconExtractor.GetIconFromExe(exePath, $"ea_{subKeyName}");
                                }
                                
                                // Clean name (sometimes has TM or R symbols)
                                string cleanName = displayName?.Replace("™", "")?.Replace("®", "")?.Trim() ?? new DirectoryInfo(installLocation).Name;

                                var game = new Game
                                {
                                    Id = $"ea_{subKeyName}",
                                    Name = cleanName,
                                    Path = $"origin2://game/launch?offerIds={subKeyName}", // Fallback URI
                                    Args = "",
                                    Platform = "EA App",
                                    LaunchType = "uri",
                                    ImageUrl = iconUrl,
                                    InstallDirectory = installLocation
                                };

                                // Wenn wir eine exakte Exe haben, versuchen wir es direkt. EA App lässt das oft zu.
                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    game.Path = exePath;
                                    game.LaunchType = "exe";
                                }

                                games.Add(game);
                                Logger.Log($"Found EA game: {cleanName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error scanning EA game {subKeyName}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error scanning EA games", ex);
            }

            return games;
        }

        private string FindPrimaryExe(string installDir)
        {
            try
            {
                var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var file in exeFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file).ToLower();
                    if (fileName.Contains("unins") || fileName.Contains("crash") || fileName.Contains("cleanup") || fileName.Contains("touchup") || fileName.Contains("eadesktop"))
                        continue;

                    return file;
                }
            }
            catch { }

            return string.Empty;
        }
    }
}
