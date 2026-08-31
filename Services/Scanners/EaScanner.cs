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

            RegistryScanUtility.ForEachSubKey(UninstallRegistryPath, (_, appKey) =>
            {
                string? displayName = appKey.GetValue("DisplayName") as string;
                string? installDirectory = appKey.GetValue("InstallLocation") as string;

                if (IsEaClient(displayName) ||
                    string.IsNullOrWhiteSpace(installDirectory))
                {
                    return;
                }

                // Dieselbe Prüfung wie beim Scan: nur Ordner mit Installationsdaten
                // von EA gehören zur Bibliothek.
                if (EaGameManifestReader.TryRead(installDirectory) == null)
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
            var seenContentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Die Registry liefert die Installationsordner; Kennung und Titel
            // stammen aus den Installationsdaten im Spielordner selbst.
            RegistryScanUtility.ForEachSubKey(UninstallRegistryPath, (subKeyName, appKey) =>
            {
                ct.ThrowIfCancellationRequested();

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

                var manifest = EaGameManifestReader.TryRead(installLocation);
                if (manifest == null)
                {
                    return;
                }

                // Dasselbe Spiel besitzt teils mehrere Deinstallationseinträge,
                // etwa zusätzlich einen von Steam angelegten. Die Inhaltskennung
                // ist die verlässliche Identität.
                if (!seenContentIds.Add(manifest.ContentId))
                {
                    return;
                }

                string gameId = $"ea_{manifest.ContentId}";
                string exePath = ExecutableSelector.FindPrimaryExecutable(
                    installLocation,
                    "unins", "crash", "cleanup", "touchup", "eadesktop");
                string iconUrl = "";

                if (!string.IsNullOrEmpty(exePath))
                {
                    iconUrl = IconExtractor.GetIconFromExe(exePath, gameId);
                }

                string cleanName = CleanTitle(manifest.Title)
                    ?? CleanTitle(displayName)
                    ?? new DirectoryInfo(installLocation).Name;

                games.Add(new Game
                {
                    Id = gameId,
                    Name = cleanName,
                    Path = BuildLaunchUri(manifest.ContentId),
                    Args = "",
                    Platform = Constants.Platforms.EAApp,
                    LaunchType = "uri",
                    ImageUrl = iconUrl,
                    InstallDirectory = installLocation
                });
                Logger.Log($"Found EA game: {cleanName} (Content-ID: {manifest.ContentId})");
            });

            return games;
        }

        internal static string BuildLaunchUri(string offerId) =>
            $"origin2://game/launch?offerIds={Uri.EscapeDataString(offerId)}";

        /// <summary>
        /// Entfernt Markensymbole und liefert null für unbrauchbare Titel, damit
        /// die Rückfallkette den nächsten Kandidaten prüfen kann.
        /// </summary>
        private static string? CleanTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string cleaned = title.Replace("™", "").Replace("®", "").Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private static bool IsEaClient(string? displayName) =>
            !string.IsNullOrWhiteSpace(displayName) &&
            (displayName.Contains("EA app", StringComparison.OrdinalIgnoreCase) ||
             displayName.Contains("Origin", StringComparison.OrdinalIgnoreCase));
    }
}
