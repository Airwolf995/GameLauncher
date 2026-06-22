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

        public static List<string> GetAutoDetectedPaths()
        {
            var paths = new List<string>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                if (key == null)
                {
                    return paths;
                }

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(subKeyName);
                    string? publisher = appKey?.GetValue("Publisher") as string;
                    string? displayName = appKey?.GetValue("DisplayName") as string;
                    string? installDirectory = appKey?.GetValue("InstallLocation") as string;

                    if (string.IsNullOrWhiteSpace(publisher) ||
                        !publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) ||
                        IsEaClient(displayName) ||
                        string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    ScannerPathUtility.AddExistingDirectory(paths, installDirectory);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("EA path detection failed", ex);
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
                                if (IsEaClient(displayName))
                                    continue;

                                string exePath = ExecutableSelector.FindPrimaryExecutable(
                                    installLocation,
                                    "unins", "crash", "cleanup", "touchup", "eadesktop");
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
                                    Path = BuildLaunchUri(subKeyName),
                                    Args = "",
                                    Platform = "EA App",
                                    LaunchType = "uri",
                                    ImageUrl = iconUrl,
                                    InstallDirectory = installLocation
                                };

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

        internal static string BuildLaunchUri(string offerId) =>
            $"origin2://game/launch?offerIds={Uri.EscapeDataString(offerId)}";

        private static bool IsEaClient(string? displayName) =>
            !string.IsNullOrWhiteSpace(displayName) &&
            (displayName.Contains("EA app", StringComparison.OrdinalIgnoreCase) ||
             displayName.Contains("Origin", StringComparison.OrdinalIgnoreCase));

    }
}
