using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    internal static class ExecutableSelector
    {
        /// <summary>
        /// Bestimmt die Startdatei eines Spielordners. Ausgewertet wird zuerst die
        /// Namensverwandtschaft zum Ordner und danach die Dateigröße, weil das
        /// Spielprogramm regelmäßig deutlich größer ist als mitgelieferte
        /// Hilfsprogramme. Die frühere rein alphabetische Auswahl traf dagegen
        /// häufig einen Launcher oder Absturzmelder.
        /// </summary>
        public static string FindPrimaryExecutable(string installDirectory, params string[] excludedNameFragments)
        {
            try
            {
                HashSet<string> exclusions = new(excludedNameFragments, StringComparer.OrdinalIgnoreCase);
                var candidates = Directory
                    .EnumerateFiles(installDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(path => !exclusions.Any(fragment =>
                        Path.GetFileNameWithoutExtension(path).Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (candidates.Count <= 1)
                {
                    return candidates.FirstOrDefault() ?? string.Empty;
                }

                string rawDirectoryName = new DirectoryInfo(installDirectory).Name;
                string directoryName = NormalizeName(rawDirectoryName);
                var directoryWords = GetSignificantWords(rawDirectoryName);

                return FirstByRelevance(candidates, path => ExecutableName(path) == directoryName)
                    ?? FirstByRelevance(candidates, path => IsRelatedName(ExecutableName(path), directoryName))
                    ?? FirstByRelevance(candidates, path => ContainsSignificantWord(ExecutableName(path), directoryWords))
                    ?? FirstByRelevance(candidates, _ => true)
                    ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Error($"Executable search failed in {installDirectory}", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Wählt aus den passenden Dateien die größte; bei Gleichstand entscheidet
        /// der Dateiname, damit das Ergebnis stabil bleibt.
        /// </summary>
        private static string? FirstByRelevance(IEnumerable<string> candidates, Func<string, bool> matches) =>
            candidates
                .Where(matches)
                .OrderByDescending(GetFileLength)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        private static long GetFileLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Vergleicht Datei- und Ordnernamen ohne Trenn- und Sonderzeichen.
        /// Kurze Namen werden ausgenommen, damit keine zufälligen Treffer entstehen.
        /// </summary>
        private static bool IsRelatedName(string executableName, string directoryName)
        {
            if (executableName.Length < 3 || directoryName.Length < 3)
            {
                return false;
            }

            return directoryName.Contains(executableName, StringComparison.Ordinal) ||
                   executableName.Contains(directoryName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Erkennt abgekürzte Startdateien wie "ACValhalla.exe" im Ordner
        /// "Assassins Creed Valhalla" anhand eines gemeinsamen aussagekräftigen Wortes.
        /// </summary>
        private static bool ContainsSignificantWord(string executableName, IReadOnlyCollection<string> directoryWords) =>
            directoryWords.Any(word => executableName.Contains(word, StringComparison.Ordinal));

        /// <summary>
        /// Zerlegt den Ordnernamen in Wörter und behält die aussagekräftigen.
        /// Kurze Wörter wie "of" oder "the" führen sonst zu Zufallstreffern.
        /// </summary>
        private static List<string> GetSignificantWords(string name) =>
            name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeName)
                .Where(word => word.Length >= 4)
                .ToList();

        private static string ExecutableName(string path) =>
            NormalizeName(Path.GetFileNameWithoutExtension(path));

        private static string NormalizeName(string name) =>
            new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
