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
            public const string UbisoftConnect = "Ubisoft Connect";
            public const string EAApp = "EA App";
            public const string Xbox = "Xbox";
            public const string Manual = "Manual";
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
        
        /// <summary>
        /// Bekannte Launcher und Store-Clients. Ein Eintrag, der auf eine dieser
        /// Programmdateien zeigt, startet zwar ein Spiel, läuft dabei aber als
        /// Prozess des Clients. Das ist sowohl für die Einstufung im Import als
        /// auch für die Spielzeiterfassung dieselbe Frage, deshalb steht die
        /// Liste hier statt in einem der beiden Aufrufer.
        ///
        /// Bewusst nur konkrete Namen: ein generisches "launcher.exe" würde auch
        /// Spiele erfassen, die tatsächlich so heißen.
        /// </summary>
        public static class Launchers
        {
            private static readonly System.Collections.Generic.HashSet<string> Executables =
                new(StringComparer.OrdinalIgnoreCase)
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
            /// Prüft, ob ein Pfad auf eine bekannte Launcher-Programmdatei zeigt.
            /// </summary>
            public static bool IsLauncherExecutable(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                return Executables.Contains(System.IO.Path.GetFileName(path));
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
            public const string All = "all";
            public const string Favorites = "favorites";
            public const string Hidden = "hidden";
            public const string Manual = "manual";
            public const string TagPrefix = "tag:";
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
                ["Blue"] = "#007ACC",
                ["Red"] = "#E51400",
                ["Green"] = "#60A917",
                ["Orange"] = "#FA6800",
                ["Purple"] = "#AA00FF",
                ["Pink"] = "#D80073",
                ["Dark"] = "#333333"
            };
            
            /// <summary>
            /// Gets the color code for a theme name, with fallback to default blue.
            /// </summary>
            public static string GetColorCodeForTheme(string themeName)
            {
                var normalized = NormalizeThemeKey(themeName);
                return ThemeColors.TryGetValue(normalized, out var color) ? color : "#007ACC";
            }

            public static string NormalizeThemeKey(string? themeName) => themeName switch
            {
                "Blau" => "Blue",
                "Rot" => "Red",
                "Grün" => "Green",
                "Orange" => "Orange",
                "Lila" => "Purple",
                "Pink" => "Pink",
                "Dunkel" => "Dark",
                "Blue" => "Blue",
                "Red" => "Red",
                "Green" => "Green",
                "Purple" => "Purple",
                "Dark" => "Dark",
                _ => "Blue"
            };

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
                [Platforms.EAApp] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F56C2D")), // Orange
                ["Ubisoft"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0070BA")), // Ubi Blue
                ["Uplay"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0070BA")), // Ubi Blue
                [Platforms.UbisoftConnect] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0070BA")), // Ubi Blue
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
