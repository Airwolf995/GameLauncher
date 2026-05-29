using System;
using System.Text.Json.Serialization;
using GameLauncher.Services;

namespace GameLauncher.Models
{
    public class UISettings
    {
        [JsonPropertyName("card_size")]
        public string CardSizeString { get; set; } = "Mittel";

        /// <summary>
        /// Typsicherer Zugriff auf CardSize. Getter/Setter konvertieren den JSON-String.
        /// </summary>
        [JsonIgnore]
        public CardSize CardSize
        {
            get => CardSizeString switch
            {
                "Klein" => CardSize.Small,
                "Groß" => CardSize.Large,
                _ => CardSize.Medium
            };
            set => CardSizeString = value switch
            {
                CardSize.Small => "Klein",
                CardSize.Large => "Groß",
                _ => "Mittel"
            };
        }
        
        [JsonPropertyName("view_mode")]
        public string ViewModeString { get; set; } = "Karten";

        /// <summary>
        /// Typsicherer Zugriff auf ViewMode. Getter/Setter konvertieren den JSON-String.
        /// </summary>
        [JsonIgnore]
        public ViewMode ViewMode
        {
            get => ViewModeString switch
            {
                "Liste" => ViewMode.List,
                _ => ViewMode.Cards
            };
            set => ViewModeString = value switch
            {
                ViewMode.List => "Liste",
                _ => "Karten"
            };
        }

        [JsonPropertyName("library_sort_mode")]
        public string LibrarySortModeString { get; set; } = "Name";

        /// <summary>
        /// Typsicherer Zugriff auf den Sortiermodus der Bibliothek.
        /// </summary>
        [JsonIgnore]
        public GameSortMode LibrarySortMode
        {
            get => Enum.TryParse<GameSortMode>(LibrarySortModeString, ignoreCase: true, out var mode)
                ? mode
                : GameSortMode.Name;
            set => LibrarySortModeString = value.ToString();
        }

        [JsonPropertyName("library_filter")]
        public string LibraryFilter { get; set; } = "Alle";
        
        [JsonPropertyName("animations_enabled")]
        public bool AnimationsEnabled { get; set; } = true;
        
        [JsonPropertyName("font_scale")]
        public double FontScale { get; set; } = 1.0; // 0.8 - 1.4
        
        [JsonPropertyName("background_image")]
        public string BackgroundImage { get; set; } = "";
        
        [JsonPropertyName("autostart_enabled")]
        public bool AutostartEnabled { get; set; } = false;

        [JsonPropertyName("auto_check_updates")]
        public bool AutoCheckUpdates { get; set; } = true;

        /// <summary>
        /// Verschlüsselter API Key (DPAPI, Base64).
        /// </summary>
        [JsonPropertyName("steamgriddb_api_key_encrypted")]
        public string EncryptedSteamGridDbApiKey { get; set; } = "";

        /// <summary>
        /// Helfer-Property für transparenten Zugriff auf den API Key.
        /// Getter entschlüsselt, Setter verschlüsselt automatisch.
        /// </summary>
        [JsonIgnore]
        public string SteamGridDbApiKey
        {
            get => SecurityService.DecryptString(EncryptedSteamGridDbApiKey);
            set => EncryptedSteamGridDbApiKey = SecurityService.EncryptString(value);
        }

        [JsonPropertyName("minimize_to_tray")]
        public bool MinimizeToTray { get; set; } = false;

        [JsonPropertyName("minimize_on_game_start")]
        public bool MinimizeOnGameStart { get; set; } = false;

        [JsonPropertyName("close_on_game_start")]
        public bool CloseOnGameStart { get; set; } = false;

        [JsonPropertyName("overlay_hotkey_ctrl")]
        public bool OverlayHotkeyCtrl { get; set; } = false;

        [JsonPropertyName("overlay_hotkey_alt")]
        public bool OverlayHotkeyAlt { get; set; } = true;

        [JsonPropertyName("overlay_hotkey_shift")]
        public bool OverlayHotkeyShift { get; set; } = false;

        [JsonPropertyName("overlay_hotkey_win")]
        public bool OverlayHotkeyWin { get; set; } = false;

        [JsonPropertyName("overlay_hotkey_key")]
        public string OverlayHotkeyKey { get; set; } = "G";

        [JsonPropertyName("first_start")]
        public bool FirstStart { get; set; } = true;
    }
}
