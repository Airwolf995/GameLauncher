using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services
{
    /// <summary>
    /// Handles loading, saving of the application configuration.
    /// Extracted from GameManager to follow Single Responsibility Principle.
    /// </summary>
    public class ConfigService : IDisposable
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _configPath;
        private GameConfig _config;
        
        // Debouncing for config saves
        private readonly System.Timers.Timer _saveTimer;
        private volatile bool _pendingSave = false;

        public GameConfig Config => _config;
        public string ConfigPath => _configPath;

        public ConfigService() : this(null) { }

        internal ConfigService(string? configPathOverride)
        {
            _configPath = ResolveConfigPath(configPathOverride, ensureDirectory: true);

            _config = LoadConfig();
            
            // Initialize save debounce timer
            _saveTimer = new System.Timers.Timer(Constants.Timings.ConfigSaveDebounceMs);
            _saveTimer.AutoReset = false;
            _saveTimer.Elapsed += (s, e) => {
                if (_pendingSave)
                {
                    SaveConfigImmediate(_config);
                    _pendingSave = false;
                }
            };
        }

        private GameConfig LoadConfig()
        {
            var defaults = new GameConfig
            {
                SteamLibraryPaths = new List<string>()
            };

            if (!File.Exists(_configPath))
            {
                try
                {
                    SaveConfigImmediate(defaults);
                    return defaults;
                }
                catch (Exception ex)
                {
                    Logger.Error("Error creating config", ex);
                    return defaults;
                }
            }

            try
            {
                string json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<GameConfig>(json);
                
                if (config == null) return defaults;

                // Ensure defaults for all collections to prevent NullReferenceExceptions
                config.SteamLibraryPaths ??= new List<string>();
                config.EpicLibraryPaths ??= new List<string>();
                config.XboxLibraryPaths ??= new List<string>();
                config.ManualGames ??= new List<Game>();
                config.Favorites ??= new HashSet<string>();
                config.LastPlayed ??= new Dictionary<string, DateTime>();
                config.PlayTime ??= new Dictionary<string, PlayTimeEntry>();
                config.IgnoredProcesses ??= new List<string>();
                config.UISettings ??= new UISettings();
                config.HiddenGames ??= new HashSet<string>();
                config.ImageOverrides ??= new Dictionary<string, string>();
                config.GameTags ??= new Dictionary<string, List<string>>();
                NormalizeConfig(config);

                Logger.Log("Configuration loaded successfully.");
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading config", ex);
                return defaults;
            }
        }

        /// <summary>
        /// Queues a debounced save. Multiple rapid calls are batched.
        /// </summary>
        public void SaveConfig()
        {
            _pendingSave = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        
        /// <summary>
        /// Saves the configuration immediately, bypassing the debounce timer.
        /// </summary>
        public void SaveConfigImmediate(GameConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, _jsonOptions);
                string? directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = _configPath + ".tmp";
                File.WriteAllText(tempPath, json);
                
                if (File.Exists(_configPath))
                {
                    File.Move(tempPath, _configPath, overwrite: true);
                }
                else
                {
                    File.Move(tempPath, _configPath);
                }

                Logger.Log("Configuration saved atomically.");
            }
            catch (Exception ex)
            {
                Logger.Error("Error saving config atomically", ex);
            }
        }

        public static string GetStoredLanguageCode(string? configPathOverride = null)
        {
            string configPath = ResolveConfigPath(configPathOverride, ensureDirectory: false);

            if (!File.Exists(configPath))
            {
                return "en";
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<GameConfig>(json);
                var languageCode = config?.UISettings?.LanguageCode;
                return string.Equals(languageCode, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            }
            catch
            {
                return "en";
            }
        }

        public void Dispose()
        {
            if (_pendingSave)
            {
                SaveConfigImmediate(_config);
                _pendingSave = false;
            }
            _saveTimer?.Dispose();
        }

        private static void NormalizeConfig(GameConfig config)
        {
            config.Theme = Constants.UI.NormalizeThemeKey(config.Theme);

            config.UISettings.CardSizeString = config.UISettings.CardSize switch
            {
                CardSize.Small => "Small",
                CardSize.Large => "Large",
                _ => "Medium"
            };

            config.UISettings.ViewModeString = config.UISettings.ViewMode switch
            {
                ViewMode.List => "List",
                _ => "Cards"
            };

            config.UISettings.LanguageCode = string.Equals(config.UISettings.LanguageCode, "de", StringComparison.OrdinalIgnoreCase)
                ? "de"
                : "en";

            config.UISettings.LibraryFilter = NormalizeFilterKey(config.UISettings.LibraryFilter);
        }

        private static string NormalizeFilterKey(string? filter) => filter switch
        {
            null or "" => Constants.Filters.All,
            "Alle" => Constants.Filters.All,
            "all" => Constants.Filters.All,
            "Favoriten" => Constants.Filters.Favorites,
            "favorites" => Constants.Filters.Favorites,
            "Ausgeblendet" => Constants.Filters.Hidden,
            "Versteckt" => Constants.Filters.Hidden,
            "hidden" => Constants.Filters.Hidden,
            "Manuell" => Constants.Filters.Manual,
            "Manual" => Constants.Filters.Manual,
            _ when filter.StartsWith("🏷️ ", StringComparison.Ordinal) => $"{Constants.Filters.TagPrefix}{filter.Substring(4)}",
            _ => filter
        };

        private static string ResolveConfigPath(string? configPathOverride, bool ensureDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configPathOverride))
            {
                if (ensureDirectory)
                {
                    var configDirectory = Path.GetDirectoryName(configPathOverride);
                    if (!string.IsNullOrWhiteSpace(configDirectory) && !Directory.Exists(configDirectory))
                    {
                        Directory.CreateDirectory(configDirectory);
                    }
                }

                return configPathOverride;
            }

#if DEBUG
            // In Debug mode, prioritize project root config for development
            string headers = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(headers, @"..\..\..\"));
            string devConfig = Path.Combine(projectRoot, "game_launcher_config.json");

            if (File.Exists(devConfig))
            {
                return devConfig;
            }
#endif

            // Fall back to Documents
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string appDataDir = Path.Combine(documentsPath, "GameLauncher");
            if (ensureDirectory && !Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }
            return Path.Combine(appDataDir, "game_launcher_config.json");
        }
    }
}
