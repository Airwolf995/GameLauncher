using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Liest die Spieltitel aus der Konfigurationsdatei von Ubisoft Connect.
    /// Die Registry kennt nur Installationsverzeichnisse, weshalb der Scanner
    /// sonst auf den Ordnernamen zurückfallen muss; diese Datei enthält die
    /// tatsächlichen Titel und ordnet sie über den Registry-Unterschlüssel zu.
    /// </summary>
    internal static class UbisoftGameNameCatalog
    {
        private const string LauncherRegistryPath = @"SOFTWARE\Ubisoft\Launcher";

        private static readonly string DefaultLauncherDirectory =
            @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher";

        private static readonly string[] ConfigurationPathParts = ["cache", "configuration", "configurations"];

        /// <summary>
        /// Die Datei enthält den gesamten Ubisoft-Katalog und ist daher spürbar
        /// größer als eine reine Installationsliste. Die Grenze verhindert, dass
        /// eine unerwartet große oder beschädigte Datei den Scan belastet.
        /// </summary>
        private const long MaxConfigurationFileSize = 32 * 1024 * 1024;

        /// <summary>
        /// Der Katalog trennt seine Einträge durch diese Versionszeile.
        /// </summary>
        private const string BlockSeparator = "version: 2.0";

        private static readonly Regex InstallIdPattern = new(
            @"Launcher\\Installs\\(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DisplayNamePattern = new(
            @"^\s*display_name:\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex NamePattern = new(
            @"^\s*name:\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// Titel, die nicht ausgeschrieben sind, verweisen als Schlüssel in den
        /// Lokalisierungsabschnitt des Eintrags.
        /// </summary>
        private static readonly Regex LocalizationKeyPattern = new(
            @"^l\d+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Liefert die Zuordnung von Registry-Unterschlüssel zu Spieltitel.
        /// Bei fehlender oder unlesbarer Datei bleibt die Zuordnung leer und der
        /// Scanner verwendet weiterhin den Ordnernamen.
        /// </summary>
        public static Dictionary<string, string> Load()
        {
            string? configurationPath = FindConfigurationFile();
            if (configurationPath == null)
            {
                return [];
            }

            try
            {
                var fileInfo = new FileInfo(configurationPath);
                if (fileInfo.Length > MaxConfigurationFileSize)
                {
                    Logger.Log($"Ubisoft-Konfiguration wird übersprungen, Datei zu groß: {fileInfo.Length} Byte");
                    return [];
                }

                // Die Datei mischt YAML-Abschnitte mit binären Trennfeldern; nicht
                // dekodierbare Bytes werden ersetzt statt die Auswertung abzubrechen.
                string content = File.ReadAllText(configurationPath, System.Text.Encoding.UTF8);
                var names = Parse(content);
                Logger.Log($"Ubisoft-Konfiguration gelesen: {names.Count} Spieltitel zugeordnet.");
                return names;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ubisoft-Konfiguration {configurationPath} konnte nicht gelesen werden", ex);
                return [];
            }
        }

        /// <summary>
        /// Wertet den Inhalt der Konfigurationsdatei aus.
        /// </summary>
        internal static Dictionary<string, string> Parse(string content)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(content))
            {
                return names;
            }

            foreach (string block in content.Split(BlockSeparator))
            {
                var installIds = InstallIdPattern.Matches(block);
                if (installIds.Count == 0)
                {
                    continue;
                }

                string? name = ReadTitle(block);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                foreach (Match installId in installIds)
                {
                    // Der erste Treffer gewinnt: Ergänzungsinhalte verweisen teils
                    // auf dieselbe Kennung wie das Hauptspiel.
                    names.TryAdd(installId.Groups[1].Value, name);
                }
            }

            return names;
        }

        private static string? ReadTitle(string block)
        {
            var displayName = DisplayNamePattern.Match(block);
            string? title = displayName.Success
                ? CleanValue(displayName.Groups[1].Value)
                : null;

            if (string.IsNullOrWhiteSpace(title))
            {
                var name = NamePattern.Match(block);
                title = name.Success ? CleanValue(name.Groups[1].Value) : null;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            if (LocalizationKeyPattern.IsMatch(title))
            {
                title = ResolveLocalizedTitle(block, title);
            }

            return RemoveTrademarkSymbols(title);
        }

        /// <summary>
        /// Löst einen Lokalisierungsschlüssel im Abschnitt "default" auf. Dieser
        /// steht im Katalog vor den übersetzten Abschnitten, weshalb der erste
        /// Treffer dahinter der gesuchte Titel ist.
        /// </summary>
        private static string? ResolveLocalizedTitle(string block, string localizationKey)
        {
            int defaultIndex = block.IndexOf("default:", StringComparison.Ordinal);
            if (defaultIndex < 0)
            {
                return null;
            }

            var match = Regex.Match(
                block[defaultIndex..],
                @"^\s*" + Regex.Escape(localizationKey) + @":\s*(.+)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);

            return match.Success ? CleanValue(match.Groups[1].Value) : null;
        }

        /// <summary>
        /// Entfernt Markensymbole aus dem Titel, damit die Bibliothek einheitlich
        /// bleibt; der EA-Scanner verfährt mit seinen Titeln ebenso.
        /// </summary>
        private static string? RemoveTrademarkSymbols(string? title) =>
            string.IsNullOrWhiteSpace(title)
                ? title
                : title.Replace("™", "").Replace("®", "").Trim();

        /// <summary>
        /// Entfernt Zeilenreste und die im Katalog uneinheitlich gesetzten
        /// Anführungszeichen.
        /// </summary>
        private static string CleanValue(string value)
        {
            string cleaned = value.Trim().TrimEnd('\r');
            if (cleaned.Length >= 2 &&
                ((cleaned[0] == '"' && cleaned[^1] == '"') ||
                 (cleaned[0] == '\'' && cleaned[^1] == '\'')))
            {
                cleaned = cleaned[1..^1].Trim();
            }

            return cleaned;
        }

        private static string? FindConfigurationFile()
        {
            var launcherDirectories = new List<string>(
                RegistryScanUtility.ReadStrings(LauncherRegistryPath, "InstallDir"))
            {
                DefaultLauncherDirectory
            };

            foreach (string directory in launcherDirectories)
            {
                try
                {
                    string path = Path.Combine(
                        directory,
                        ConfigurationPathParts[0],
                        ConfigurationPathParts[1],
                        ConfigurationPathParts[2]);

                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    Logger.Log($"Ubisoft-Launcher-Pfad wird übersprungen: {ex.GetType().Name}");
                }
            }

            return null;
        }
    }
}
