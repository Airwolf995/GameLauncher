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
        public string PlatformName => Constants.Platforms.EAApp;

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

                    if (IsEaClient(displayName) ||
                        string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    if (!IsEaGameInstallation(publisher, installDirectory))
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

                                if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                                    continue;

                                // Ignoriere den EA-Launcher selbst
                                if (IsEaClient(displayName))
                                    continue;

                                if (!IsEaGameInstallation(publisher, installLocation))
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
                                string cleanName = string.IsNullOrWhiteSpace(displayName)
                                    ? new DirectoryInfo(installLocation).Name
                                    : displayName.Replace("™", "").Replace("®", "").Trim();
                                if (string.IsNullOrWhiteSpace(cleanName))
                                {
                                    cleanName = subKeyName;
                                }

                                var game = new Game
                                {
                                    Id = $"ea_{subKeyName}",
                                    Name = cleanName,
                                    Path = BuildLaunchUri(subKeyName),
                                    Args = "",
                                    Platform = Constants.Platforms.EAApp,
                                    LaunchType = "uri",
                                    ImageUrl = iconUrl,
                                    InstallDirectory = installLocation
                                };

                                games.Add(game);
                                Logger.Log($"Found EA game: {cleanName}");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error scanning EA game {subKeyName}", ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
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

        internal static bool IsEaGameInstallation(string? publisher, string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            try
            {
                // Die Installer-Metadaten liegen im Spielordner und sind unabhängig
                // von uneinheitlichen Publisher-Strings in der Uninstall-Registry.
                if (File.Exists(Path.Combine(installDirectory, "__Installer", "installerdata.xml")))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }

            // Ältere Installationen besitzen nicht immer die Metadatendatei.
            return !string.IsNullOrWhiteSpace(publisher) &&
                   publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase);
        }

    }
}
