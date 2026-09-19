using System;
using System.IO;
using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Services.Settings
{
    internal enum ConfigTransferResult
    {
        Success,

        /// <summary>Die Datei liegt nicht vor oder liess sich nicht lesen.</summary>
        SourceUnreadable,

        /// <summary>Die Datei ist keine Konfiguration dieser Anwendung.</summary>
        NotAConfigFile,

        /// <summary>Das Ziel liess sich nicht schreiben.</summary>
        WriteFailed
    }

    /// <summary>
    /// Sichert die Konfiguration in eine frei gewaehlte Datei und spielt sie von
    /// dort wieder ein.
    ///
    /// Favoriten, Schlagwoerter, manuelle Eintraege und vor allem die gesammelte
    /// Spielzeit liegen ausschliesslich in der Konfigurationsdatei im
    /// Benutzerprofil. Geht sie verloren, sind diese Angaben nicht
    /// wiederherstellbar; ein Rechnerwechsel liess sie bisher ebenfalls zurueck.
    /// </summary>
    internal sealed class ConfigTransferService : IConfigTransferService
    {
        /// <summary>
        /// So viele bekannte Felder muessen vorkommen, damit eine Datei als
        /// Sicherung gilt.
        ///
        /// Ein einzelnes Feld genuegt bewusst nicht: unter den Namen sind mit
        /// "theme" und "favorites" zwei sehr gebraeuchliche, die auch in der
        /// Konfigurationsdatei einer voellig anderen Anwendung stehen koennen.
        /// Eine solche Datei ginge sonst als Sicherung durch und loeschte beim
        /// Einspielen den gesamten Bestand.
        ///
        /// Fuer echte Sicherungen ist die Huerde folgenlos: sie entstehen durch
        /// Serialisieren der vollstaendigen Konfiguration und enthalten daher
        /// immer alle Felder.
        /// </summary>
        private const int RequiredConfigPropertyMatches = 3;

        /// <summary>
        /// Eigenschaftsnamen aus <see cref="GameConfig"/>.
        ///
        /// Ohne diese Pruefung genuegte die Zeichenfolge "{}", um eine gueltige,
        /// aber vollstaendig leere Konfiguration zu erzeugen - die falsche Datei
        /// im Auswahldialog haette damit die gesamte Spielzeit stillschweigend
        /// geloescht.
        /// </summary>
        private static readonly string[] KnownConfigProperties =
        [
            "steam_library_paths",
            "epic_library_paths",
            "xbox_library_paths",
            "manual_games",
            "favorites",
            "last_played",
            "play_time",
            "ignored_processes",
            "image_overrides",
            "hidden_games",
            "game_tags",
            "theme",
            "ui_settings"
        ];

        private readonly ConfigService _configService;

        public ConfigTransferService(ConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Schreibt den aktuellen Stand in die angegebene Datei. Ausgegeben wird
        /// bewusst der Stand aus dem Speicher und nicht die Datei auf der
        /// Festplatte: Letztere wird verzoegert geschrieben und waere sonst
        /// moeglicherweise aelter als das, was der Benutzer gerade sieht.
        /// </summary>
        public ConfigTransferResult Export(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return ConfigTransferResult.WriteFailed;
            }

            string? json = _configService.TrySerializeCurrentConfig();
            if (json == null)
            {
                return ConfigTransferResult.WriteFailed;
            }

            try
            {
                File.WriteAllText(targetPath, json);
                Logger.Log($"Konfiguration wurde gesichert: {targetPath}");
                return ConfigTransferResult.Success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Konfiguration konnte nicht nach {targetPath} gesichert werden", ex);
                return ConfigTransferResult.WriteFailed;
            }
        }

        /// <summary>
        /// Spielt eine gesicherte Konfiguration ein. Der bisherige Stand wird
        /// zuvor daneben gesichert, danach wird jedes weitere Schreiben
        /// angehalten - die Anwendung fuehrt noch die alte Konfiguration im
        /// Speicher und wuerde die eingespielte sonst wieder ueberschreiben.
        /// Wirksam wird der neue Stand deshalb erst nach einem Neustart.
        /// </summary>
        public ConfigTransferResult Import(string sourcePath)
        {
            string json;
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return ConfigTransferResult.SourceUnreadable;
                }

                json = File.ReadAllText(sourcePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Gesicherte Konfiguration {sourcePath} konnte nicht gelesen werden", ex);
                return ConfigTransferResult.SourceUnreadable;
            }

            if (!IsConfigFile(json))
            {
                Logger.Log($"Gesicherte Konfiguration abgelehnt, keine Konfiguration dieser Anwendung: {sourcePath}");
                return ConfigTransferResult.NotAConfigFile;
            }

            if (!TryBackupCurrentConfig())
            {
                return ConfigTransferResult.WriteFailed;
            }

            try
            {
                File.Copy(sourcePath, _configService.ConfigPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error("Gesicherte Konfiguration konnte nicht eingespielt werden", ex);
                return ConfigTransferResult.WriteFailed;
            }

            StampImportTime();

            _configService.SuspendSaving();
            Logger.Log($"Konfiguration wurde eingespielt: {sourcePath}. Sie wird mit dem naechsten Start wirksam.");
            return ConfigTransferResult.Success;
        }

        /// <summary>
        /// Prueft, ob der Inhalt eine Konfiguration dieser Anwendung ist. Geprueft
        /// wird die Struktur, nicht der Inhalt: eine Konfiguration darf durchaus
        /// leere Listen enthalten, aber nicht saemtliche Felder vermissen lassen.
        /// </summary>
        internal static bool IsConfigFile(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                int matches = 0;
                foreach (string propertyName in KnownConfigProperties)
                {
                    if (document.RootElement.TryGetProperty(propertyName, out _) &&
                        ++matches >= RequiredConfigPropertyMatches)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Setzt das Aenderungsdatum der eingespielten Konfiguration auf jetzt.
        ///
        /// File.Copy uebernimmt das Datum der Quelle: die Konfiguration truege
        /// sonst den Zeitpunkt, zu dem die Sicherung entstand, und saehe im
        /// Ordner aelter aus, als der Import war - als haette er nie
        /// stattgefunden.
        ///
        /// Ein Fehlschlag darf den Import nicht scheitern lassen. Die Datei ist
        /// an dieser Stelle bereits ersetzt; ein gemeldeter Fehlschlag wuerde
        /// das Anhalten des Speicherns verhindern, und die Anwendung
        /// ueberschriebe die eingespielte Datei wieder mit ihrem alten Stand.
        /// Ein falsches Datum ist demgegenueber belanglos.
        /// </summary>
        private void StampImportTime()
        {
            try
            {
                File.SetLastWriteTime(_configService.ConfigPath, DateTime.Now);
            }
            catch (Exception ex)
            {
                Logger.Log($"Aenderungsdatum der eingespielten Konfiguration konnte nicht gesetzt werden: {ex.GetType().Name}");
            }
        }

        /// <summary>
        /// Sichert den bisherigen Stand neben der Konfigurationsdatei. Gibt es
        /// noch keine Datei, ist nichts zu sichern.
        /// </summary>
        private bool TryBackupCurrentConfig()
        {
            try
            {
                if (!File.Exists(_configService.ConfigPath))
                {
                    return true;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backupPath = $"{_configService.ConfigPath}.vor-import-{timestamp}.bak";
                File.Copy(_configService.ConfigPath, backupPath, overwrite: false);
                Logger.Log($"Bisherige Konfiguration wurde vor dem Import gesichert: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Bisherige Konfiguration konnte vor dem Import nicht gesichert werden; Import abgebrochen", ex);
                return false;
            }
        }
    }
}
