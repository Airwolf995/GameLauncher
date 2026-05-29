using System;

namespace GameLauncher
{
    public static class Constants
    {
        public static class Platforms
        {
            public const string Steam = "Steam";
            public const string Epic = "Epic Games";
            public const string GOG = "GOG";
            public const string Manual = "Manuell";
            public const string BattleNet = "Battle.net";
            
            /// <summary>
            /// Checks if the given platform string represents Epic Games.
            /// Handles both "Epic Games" and "Epic" variants.
            /// </summary>
            public static bool IsEpicPlatform(string platform)
            {
                if (string.IsNullOrEmpty(platform)) return false;
                return platform.Equals(Epic, StringComparison.OrdinalIgnoreCase) || 
                       platform.Equals("Epic", StringComparison.OrdinalIgnoreCase);
            }
        }
        
        public static class Timings
        {
            /// <summary>Config save debounce delay in milliseconds</summary>
            public const int ConfigSaveDebounceMs = 2000;
            
            /// <summary>Tray balloon tip display duration in milliseconds</summary>
            public const int TrayBalloonDurationMs = 1000;
        }
        
        public static class Filters
        {
            public const string All = "Alle";
            public const string Favorites = "Favoriten";
            public const string Hidden = "Versteckt";
            // Platform filters (Steam, GOG, Epic, Manual) are defined in Platforms class
        }
        
        public static class UI
        {
            // Card sizes (Medium - default)
            public const double CardWidthMedium = 420;
            public const double CardHeightMedium = 114;
            public const double ImageWidthMedium = 110;
            public const double ImageHeightMedium = 51;
            public const double TitleFontSizeMedium = 16;
            public const double PlatformFontSizeMedium = 12;
            
            // Card sizes (Small)
            public const double CardWidthSmall = 350;
            public const double CardHeightSmall = 92;
            public const double ImageWidthSmall = 85;
            public const double ImageHeightSmall = 40;
            public const double TitleFontSizeSmall = 14;
            public const double PlatformFontSizeSmall = 11;
            
            // Card sizes (Large)
            public const double CardWidthLarge = 500;
            public const double CardHeightLarge = 141;
            public const double ImageWidthLarge = 140;
            public const double ImageHeightLarge = 65;
            public const double TitleFontSizeLarge = 18;
            public const double PlatformFontSizeLarge = 14;
            
            /// <summary>
            /// Theme color codes for accent colors. 
            /// Consolidates the previously duplicated GetColorCodeForTheme methods.
            /// </summary>
            public static readonly System.Collections.Generic.Dictionary<string, string> ThemeColors = new()
            {
                ["Blau"] = "#007ACC",
                ["Rot"] = "#E51400",
                ["Grün"] = "#60A917",
                ["Orange"] = "#FA6800",
                ["Lila"] = "#AA00FF",
                ["Pink"] = "#D80073",
                ["Dunkel"] = "#333333"
            };
            
            /// <summary>
            /// Gets the color code for a theme name, with fallback to default blue.
            /// </summary>
            public static string GetColorCodeForTheme(string themeName)
            {
                return ThemeColors.TryGetValue(themeName, out var color) ? color : "#007ACC";
            }

            /// <summary>
            /// Cached SolidColorBrushes for platforms to avoid expensive string-to-brush conversions on every UI update.
            /// </summary>
            public static readonly System.Collections.Generic.Dictionary<string, System.Windows.Media.SolidColorBrush> PlatformBrushes = new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Steam"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC")), // Blue
                ["Xbox"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#107C10")), // Green
                ["Epic"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333333")), // Dark
                ["Epic Games"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333333")), // Dark
                ["Battle.net"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#148EFF")), // Azure
                ["Origin"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F56C2D")), // Orange
                ["EA"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F56C2D")), // Orange
                ["Ubisoft"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0070BA")), // Ubi Blue
                ["Uplay"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0070BA")), // Ubi Blue
                ["GOG"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#863286")), // Purple
                ["Riot"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D32936")) // Red
            };
        }
        
        public static class Tags
        {
            /// <summary>Default available tags for game categorization</summary>
            public static readonly string[] DefaultTags = {
                "Action", "RPG", "Strategie", "Shooter", "Indie",
                "Multiplayer", "Singleplayer", "Abgeschlossen", "In Bearbeitung",
                "Simulation", "Rennspiele", "Extraction Shooter", "Survival", "Server", "Tools"
            };
        }
    }
}
