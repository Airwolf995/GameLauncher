using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Liest die Installationsdaten, die EA im Spielordner ablegt. Die Datei
    /// enthält die Inhaltskennung, mit der sich das Spiel starten lässt, sowie
    /// den Titel in mehreren Sprachen. Die Windows-Deinstallationsregistrierung
    /// führt stattdessen nur eine Installationskennung, die sich nicht zum
    /// Starten eignet.
    /// </summary>
    internal static class EaGameManifestReader
    {
        private static readonly string[] ManifestPathParts = ["__Installer", "installerdata.xml"];

        /// <summary>
        /// Die Datei enthält den vollständigen Installationsbauplan und wächst mit
        /// der Anzahl der Zusatzinhalte. Die Grenze schützt vor einem beschädigten
        /// oder unerwartet großen Manifest.
        /// </summary>
        private const long MaxManifestFileSize = 16 * 1024 * 1024;

        /// <summary>
        /// Liest das Manifest eines Spielordners. Liefert null, wenn kein
        /// verwertbares Manifest vorliegt.
        /// </summary>
        public static EaGameManifest? TryRead(string installDirectory)
        {
            string manifestPath;
            try
            {
                manifestPath = Path.Combine(installDirectory, ManifestPathParts[0], ManifestPathParts[1]);
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                if (new FileInfo(manifestPath).Length > MaxManifestFileSize)
                {
                    Logger.Log($"EA-Manifest wird übersprungen, Datei zu groß: {manifestPath}");
                    return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            try
            {
                return Parse(File.ReadAllText(manifestPath), LocalizationService.Instance.CurrentLanguage);
            }
            catch (Exception ex)
            {
                Logger.Error($"EA-Manifest {manifestPath} konnte nicht gelesen werden", ex);
                return null;
            }
        }

        /// <summary>
        /// Wertet ein Manifest aus. Unterstützt beide vorkommenden Aufbauten:
        /// den neueren mit gameTitle-Elementen und den älteren mit localeInfo.
        /// </summary>
        internal static EaGameManifest? Parse(string xml, AppLanguage language)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }

            // Die erste Kennung bezeichnet das Spiel selbst; die weiteren gehören
            // zu Zusatzinhalten.
            string? contentId = document
                .Descendants("contentID")
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (string.IsNullOrWhiteSpace(contentId))
            {
                return null;
            }

            return new EaGameManifest(contentId, ReadTitle(document, language));
        }

        private static string? ReadTitle(XDocument document, AppLanguage language)
        {
            string preferredLocale = language == AppLanguage.German ? "de_DE" : "en_US";

            return ReadGameTitle(document, preferredLocale)
                ?? ReadLocaleInfoTitle(document, preferredLocale)
                ?? ReadGameTitle(document, "en_US")
                ?? ReadLocaleInfoTitle(document, "en_US")
                ?? ReadGameTitle(document, null)
                ?? ReadLocaleInfoTitle(document, null);
        }

        /// <summary>
        /// Neueres Format: gameTitles/gameTitle mit Sprachkennzeichen.
        /// </summary>
        private static string? ReadGameTitle(XDocument document, string? locale) =>
            document
                .Descendants("gameTitle")
                .Where(element => locale == null || MatchesLocale(element, locale))
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        /// <summary>
        /// Älteres Format: metadata/localeInfo mit untergeordnetem title.
        /// </summary>
        private static string? ReadLocaleInfoTitle(XDocument document, string? locale) =>
            document
                .Descendants("localeInfo")
                .Where(element => locale == null || MatchesLocale(element, locale))
                .Select(element => element.Element("title")?.Value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static bool MatchesLocale(XElement element, string locale) =>
            string.Equals(element.Attribute("locale")?.Value, locale, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Startkennung und Titel eines EA-Spiels aus dessen Installationsdaten.
    /// </summary>
    internal sealed record EaGameManifest(string ContentId, string? Title);
}
