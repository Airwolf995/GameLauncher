using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans EA App (formerly Origin) for installed games.
    /// </summary>
    public class EaScanner : IPlatformScanner
    {
        public string PlatformName => Constants.Platforms.EAApp;

        private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<string> GetAutoDetectedPaths()
        {
            var paths = new List<string>();

            RegistryScanUtility.ForEachSubKey(UninstallRegistryPath, (subKeyName, appKey) =>
            {
                if (IsSteamManagedEntry(subKeyName))
                {
                    return;
                }

                string? publisher = appKey.GetValue("Publisher") as string;
                string? displayName = appKey.GetValue("DisplayName") as string;
                string? installDirectory = appKey.GetValue("InstallLocation") as string;

                if (IsEaClient(displayName) ||
                    string.IsNullOrWhiteSpace(installDirectory))
                {
                    return;
                }

                if (!IsEaGameInstallation(publisher, installDirectory))
                {
                    return;
                }

                ScannerPathUtility.AddExistingDirectory(paths, installDirectory);
            });

            return ScannerPathUtility.GetLibraryDirectories(paths);
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            // EA App speichert Spiele in der Uninstall Registry
            RegistryScanUtility.ForEachSubKey(UninstallRegistryPath, (subKeyName, appKey) =>
            {
                ct.ThrowIfCancellationRequested();

                if (IsSteamManagedEntry(subKeyName))
                {
                    return;
                }

                string? publisher = appKey.GetValue("Publisher") as string;
                string? installLocation = appKey.GetValue("InstallLocation") as string;
                string? displayName = appKey.GetValue("DisplayName") as string;

                if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                {
                    return;
                }

                // Ignoriere den EA-Launcher selbst
                if (IsEaClient(displayName))
                {
                    return;
                }

                if (!IsEaGameInstallation(publisher, installLocation))
                {
                    return;
                }

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

                games.Add(new Game
                {
                    Id = $"ea_{subKeyName}",
                    Name = cleanName,
                    Path = BuildLaunchUri(subKeyName),
                    Args = "",
                    Platform = Constants.Platforms.EAApp,
                    LaunchType = "uri",
                    ImageUrl = iconUrl,
                    InstallDirectory = installLocation
                });
                Logger.Log($"Found EA game: {cleanName}");
            });

            return games;
        }

        internal static string BuildLaunchUri(string offerId) =>
            $"origin2://game/launch?offerIds={Uri.EscapeDataString(offerId)}";

        /// <summary>
        /// Steam legt für seine Spiele eigene Deinstallationseinträge unter dem
        /// Namen "Steam App &lt;AppID&gt;" an. Über Steam bezogene EA-Titel bringen die
        /// Installer-Metadaten von EA mit und würden hier sonst ein zweites Mal
        /// auftauchen, obwohl der Steam-Scanner sie bereits führt.
        /// </summary>
        internal static bool IsSteamManagedEntry(string subKeyName) =>
            subKeyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase);

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
