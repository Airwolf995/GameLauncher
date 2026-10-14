using System;
using System.IO;
using System.Linq;
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
            "play_time_by_day",
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

            if (!_configService.TryImportConfig(json))
            {
                return ConfigTransferResult.WriteFailed;
            }

            Logger.Log($"Konfiguration wurde eingespielt: {sourcePath}. Sie wird mit dem nächsten Start wirksam.");
            return ConfigTransferResult.Success;
        }

        /// <summary>
        /// Prüft, ob der Inhalt eine Konfiguration dieser Anwendung ist. Geprüft
        /// werden die bekannten Felder und ihre Deserialisierbarkeit. Leere Listen
        /// und fehlende optionale Felder bleiben für ältere Sicherungen zulässig.
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
                        // Das Modell prüft auch die Typen und verschachtelten Felder.
                        // Der Spielzeit-Konverter toleriert beim normalen Laden defekte
                        // Einträge; beim Import dürfen diese nicht unbemerkt entfallen.
                        var config = document.RootElement.Deserialize<GameConfig>();
                        if (config == null || config.ManualGames?.Exists(game => game == null || string.IsNullOrWhiteSpace(game.Id)) == true ||
                            config.GameTags?.Values.Any(tags => tags == null) == true)
                        {
                            return false;
                        }

                        if (document.RootElement.TryGetProperty("play_time", out var playTime) &&
                            playTime.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var entry in playTime.EnumerateObject())
                            {
                                if (!Serialization.PlayTimeDictionaryJsonConverter.TryReadEntry(
                                    entry.Value, JsonSerializerOptions.Default, out _))
                                {
                                    return false;
                                }
                            }
                        }

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
    }
}
