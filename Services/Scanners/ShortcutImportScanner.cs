using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Durchsucht Startmenü und Desktop nach Spielverknüpfungen. Plattformen ohne
    /// eigenen Scanner (Battle.net, Rockstar, Riot, Amazon Games, itch.io und
    /// weitere) legen dort Verknüpfungen an; sie sind damit erreichbar, ohne für
    /// jeden Anbieter einen eigenen Scanner zu pflegen.
    /// </summary>
    internal static class ShortcutImportScanner
    {
        /// <summary>
        /// Dateinamen bekannter Launcher und Store-Clients. Diese sollen als
        /// Programm gestartet werden, nicht als Spiel in der Bibliothek stehen.
        /// Bewusst nur konkrete Namen: ein generisches "launcher.exe" würde auch
        /// Spiele ausschließen, die tatsächlich so starten. Ein zu viel
        /// angebotener Treffer kostet ein Häkchen, ein fehlendes Spiel fällt
        /// dem Benutzer dagegen nicht als Filterfehler auf.
        /// </summary>
        private static readonly HashSet<string> LauncherExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam.exe",
            "epicgameslauncher.exe",
            "galaxyclient.exe",
            "eadesktop.exe",
            "origin.exe",
            "upc.exe",
            "ubisoftconnect.exe",
            "battle.net.exe",
            "battle.net launcher.exe",
            "riotclientservices.exe",
            "leagueclient.exe",
            "rockstarservice.exe",
            "playnite.desktopapp.exe",
            "itch.exe",
            "amazon games.exe",
            "gamelauncher.exe"
        };

        /// <summary>
        /// Namensbestandteile, die auf Hilfsprogramme statt auf ein Spiel hindeuten.
        /// Geprüft wird sowohl der Name der Verknüpfung als auch der Zieldatei.
        /// </summary>
        private static readonly string[] HelperNameFragments =
        [
            "uninstall",
            "deinstall",
            "entfernen",
            "unins",
            "updater",
            "error reporter",
            "setup",
            "installer",
            "repair",
            "crashreport",
            "crashhandler",
            "crash reporter",
            "readme",
            "liesmich",
            "handbuch",
            "manual",
            "support",
            "benchmark",
            "dedicated server",
            "server starten",
            "config",
            "konfiguration",
            "settings",
            "einstellungen",
            "webseite",
            "website",
            "homepage"
        ];

        /// <summary>
        /// Sammelt alle Verknüpfungen, die als Spiel infrage kommen.
        /// </summary>
        public static List<ShortcutGameCandidate> FindCandidates(CancellationToken ct = default)
        {
            var candidates = new List<ShortcutGameCandidate>();
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in GetShortcutDirectories())
            {
                ct.ThrowIfCancellationRequested();

                foreach (string shortcutPath in EnumerateShortcuts(directory))
                {
                    ct.ThrowIfCancellationRequested();

                    var candidate = TryCreateCandidate(shortcutPath);
                    if (candidate != null && seenTargets.Add(candidate.TargetPath))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            Logger.Log(
                $"Verknüpfungssuche abgeschlossen: {candidates.Count} Programm(e) gefunden, " +
                $"davon {candidates.Count(candidate => candidate.IsLikelyGame)} als Spiel eingestuft.");
            return candidates
                .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static ShortcutGameCandidate? TryCreateCandidate(string shortcutPath)
        {
            string displayName = Path.GetFileNameWithoutExtension(shortcutPath);

            var target = ShortcutResolver.TryResolve(shortcutPath);
            if (target == null || !IsSupportedTarget(target.TargetPath))
            {
                return null;
            }

            return new ShortcutGameCandidate(
                displayName,
                target.TargetPath,
                target.Arguments,
                target.WorkingDirectory,
                IsLikelyGame(displayName, target.TargetPath, target.Arguments));
        }

        /// <summary>
        /// Prüft, ob eine Verknüpfung überhaupt ein startbares Programm bezeichnet.
        /// Nur solche Ziele werden angeboten, auch in der vollständigen Ansicht.
        /// </summary>
        internal static bool IsSupportedTarget(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) ||
                !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !IsWindowsComponent(targetPath);
        }

        /// <summary>
        /// Schätzt ein, ob ein Programm ein Spiel ist. Das Ergebnis steuert nur die
        /// Vorauswahl im Dialog: Was hier als unwahrscheinlich gilt, bleibt über die
        /// vollständige Ansicht erreichbar. Eine Fehleinschätzung kostet den Benutzer
        /// daher einen Umschaltvorgang und nicht das Spiel.
        /// </summary>
        internal static bool IsLikelyGame(string shortcutName, string targetPath, string arguments)
        {
            string fileName = Path.GetFileName(targetPath);
            if (IsHelperName(shortcutName) || IsHelperName(fileName))
            {
                return false;
            }

            // Ein Store-Client mit Startparametern startet ein bestimmtes Spiel und
            // nicht sich selbst, etwa GalaxyClient.exe mit /command=runGame.
            return !LauncherExecutables.Contains(fileName) || !string.IsNullOrWhiteSpace(arguments);
        }

        /// <summary>
        /// Erkennt Hilfsprogramme anhand ihres Namens.
        /// </summary>
        internal static bool IsHelperName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return HelperNameFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Blendet Programme aus dem Windows-Verzeichnis aus; dort liegen
        /// Systemwerkzeuge, aber keine Spiele.
        /// </summary>
        private static bool IsWindowsComponent(string targetPath)
        {
            try
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (string.IsNullOrEmpty(windowsDirectory))
                {
                    return false;
                }

                return ScannerPathUtility.Normalize(targetPath)
                    .StartsWith(ScannerPathUtility.Normalize(windowsDirectory) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return true;
            }
        }

        /// <summary>
        /// Prüft, ob ein Kandidat bereits als Spiel in der Bibliothek steht: über
        /// den Namen, den identischen Startpfad oder ein bekanntes Installationsverzeichnis.
        /// </summary>
        internal static bool IsAlreadyKnown(ShortcutGameCandidate candidate, IEnumerable<Game> existingGames)
        {
            foreach (var game in existingGames)
            {
                if (!string.IsNullOrWhiteSpace(game.Name) &&
                    string.Equals(game.Name.Trim(), candidate.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(game.Path) &&
                    string.Equals(game.Path, candidate.TargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (IsInsideDirectory(candidate.TargetPath, game.InstallDirectory))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInsideDirectory(string filePath, string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !ScannerPathUtility.TryNormalize(directory, out string normalizedDirectory) ||
                !ScannerPathUtility.TryNormalize(filePath, out string normalizedFile))
            {
                return false;
            }

            return normalizedFile.StartsWith(
                normalizedDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetShortcutDirectories()
        {
            var folders = new[]
            {
                Environment.SpecialFolder.CommonStartMenu,
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.DesktopDirectory
            };

            return folders
                .Select(Environment.GetFolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateShortcuts(string directory)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 6
            };

            try
            {
                return Directory.EnumerateFiles(directory, "*.lnk", options).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error($"Verknüpfungen in {directory} konnten nicht gelesen werden", ex);
                return [];
            }
        }
    }

    /// <summary>
    /// Über eine Verknüpfung gefundenes, noch nicht importiertes Programm.
    /// <paramref name="IsLikelyGame"/> steuert, ob es in der Standardansicht des
    /// Import-Dialogs erscheint oder erst beim Anzeigen aller Programme.
    /// </summary>
    internal sealed record ShortcutGameCandidate(
        string Name,
        string TargetPath,
        string Arguments,
        string WorkingDirectory,
        bool IsLikelyGame);
}
