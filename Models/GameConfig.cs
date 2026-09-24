using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GameLauncher.Services.Serialization;

namespace GameLauncher.Models
{
    public class GameConfig
    {
        [JsonPropertyName("steam_library_paths")]
        public List<string> SteamLibraryPaths { get; set; } = new List<string>();

        [JsonPropertyName("epic_library_paths")]
        public List<string> EpicLibraryPaths { get; set; } = new List<string>();

        [JsonPropertyName("xbox_library_paths")]
        public List<string> XboxLibraryPaths { get; set; } = new List<string>();

        [JsonPropertyName("manual_games")]
        public List<Game> ManualGames { get; set; } = new List<Game>();

        [JsonPropertyName("favorites")]
        public HashSet<string> Favorites { get; set; } = new HashSet<string>();

        [JsonPropertyName("last_played")]
        public Dictionary<string, DateTime> LastPlayed { get; set; } = new Dictionary<string, DateTime>();

        [JsonPropertyName("play_time")]
        [JsonConverter(typeof(PlayTimeDictionaryJsonConverter))]
        public Dictionary<string, PlayTimeEntry> PlayTime { get; set; } = new Dictionary<string, PlayTimeEntry>();

        /// <summary>
        /// Spielzeit in Sekunden je Spiel und Kalendertag ("yyyy-MM-dd"); nur die
        /// letzten <see cref="Services.PlayTimeHistory.RetainedDays"/> Tage bleiben erhalten.
        /// </summary>
        [JsonPropertyName("play_time_by_day")]
        public Dictionary<string, Dictionary<string, int>> PlayTimeByDay { get; set; } = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>
        /// Gilt nur für neue Konfigurationen; eine gespeicherte Liste ersetzt den
        /// Standard vollständig. Die großen Store-Clients stehen nicht darin: Sie
        /// sind über <see cref="Constants.Launchers"/> bereits von der Zuordnung
        /// ausgenommen. Der BsgLauncher fehlt dort und bleibt deshalb hier.
        /// </summary>
        [JsonPropertyName("ignored_processes")]
        public List<string> IgnoredProcesses { get; set; } = new List<string> { "BsgLauncher.exe" };

        [JsonPropertyName("image_overrides")]
        public Dictionary<string, string> ImageOverrides { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("hidden_games")]
        public HashSet<string> HiddenGames { get; set; } = new HashSet<string>();

        [JsonPropertyName("game_tags")]
        public Dictionary<string, List<string>> GameTags { get; set; } = new Dictionary<string, List<string>>();

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Blue";

        [JsonPropertyName("ui_settings")]
        public UISettings UISettings { get; set; } = new UISettings();
    }
}
