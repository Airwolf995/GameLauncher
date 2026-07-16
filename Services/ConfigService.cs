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
        private const int MaxAutomaticSaveRetries = 3;
        private const double SaveRetryBackoffFactor = 2;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _configPath;
        private readonly object _saveSync = new();
        private readonly Func<GameConfig, string> _serializeConfig;
        private readonly Func<DateTime> _utcNow;
        private readonly double _saveDebounceMs;
        private GameConfig _config;
        
        // Debouncing for config saves
        private readonly System.Timers.Timer _saveTimer;
        private bool _pendingSave;
        private long _saveVersion;
        private DateTime _saveNotBeforeUtc;
        private int _automaticSaveRetryCount;
        private bool _disposed;
        private bool _canOverwriteConfig = true;

        public GameConfig Config => _config;
        public string ConfigPath => _configPath;

        public ConfigService() : this(null) { }

        internal ConfigService(
            string? configPathOverride,
            Func<GameConfig, string>? serializeConfig = null,
            double? saveDebounceMs = null,
            Func<DateTime>? utcNow = null)
        {
            _configPath = ResolveConfigPath(configPathOverride, ensureDirectory: true);
            _serializeConfig = serializeConfig ?? (config => JsonSerializer.Serialize(config, _jsonOptions));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _saveDebounceMs = saveDebounceMs ?? Constants.Timings.ConfigSaveDebounceMs;

            _config = LoadConfig();
            
            // Initialize save debounce timer
            _saveTimer = new System.Timers.Timer(_saveDebounceMs);
            _saveTimer.AutoReset = false;
            _saveTimer.Elapsed += (_, _) => FlushPendingSave();
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
                _canOverwriteConfig = TryBackupInvalidConfig();
                return defaults;
            }
        }

        /// <summary>
        /// Queues a debounced save. Multiple rapid calls are batched.
        /// </summary>
        public void SaveConfig()
        {
            lock (_saveSync)
            {
                if (_disposed || !_canOverwriteConfig)
                {
                    return;
                }

                _pendingSave = true;
                _saveVersion++;
                _automaticSaveRetryCount = 0;
                ScheduleSaveTimer(_saveDebounceMs);
            }
        }
        
        /// <summary>
        /// Saves the configuration immediately, bypassing the debounce timer.
        /// </summary>
        public void SaveConfigImmediate(GameConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            lock (_saveSync)
            {
                if (_disposed || !_canOverwriteConfig)
                {
                    return;
                }

                _saveVersion++;
                bool savesCurrentConfig = ReferenceEquals(config, _config);
                if (savesCurrentConfig)
                {
                    _pendingSave = true;
                    _automaticSaveRetryCount = 0;
                    _saveTimer.Stop();
                }

                string? json = TrySerializeConfig(config);
                if (json == null || !TryWriteSerializedConfig(json))
                {
                    if (savesCurrentConfig)
                    {
                        ScheduleSaveRetry();
                    }
                    return;
                }

                if (savesCurrentConfig)
                {
                    _pendingSave = false;
                    _automaticSaveRetryCount = 0;
                }
            }
        }

        private string? TrySerializeConfig(GameConfig config)
        {
            try
            {
                return _serializeConfig(config);
            }
            catch (Exception ex)
            {
                Logger.Error("Konfiguration konnte nicht serialisiert werden", ex);
                return null;
            }
        }

        private bool TryWriteSerializedConfig(string json)
        {
            try
            {
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

                Logger.Log("Konfiguration wurde atomar gespeichert.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Konfiguration konnte nicht atomar gespeichert werden", ex);
                return false;
            }
        }

        internal void FlushPendingSave()
        {
            _saveTimer.Stop();
            long versionToSave;
            lock (_saveSync)
            {
                if (_disposed || !_pendingSave || !_canOverwriteConfig)
                {
                    return;
                }

                double remainingDelayMs = (_saveNotBeforeUtc - _utcNow()).TotalMilliseconds;
                if (remainingDelayMs > 0)
                {
                    StartSaveTimer(remainingDelayMs);
                    return;
                }

                versionToSave = _saveVersion;
            }

            // Die potenziell teure Serialisierung läuft auf dem Timer-Thread und
            // blockiert dadurch keine normalen SaveConfig()-Aufrufer.
            string? json = TrySerializeConfig(_config);

            lock (_saveSync)
            {
                if (_disposed || !_pendingSave || !_canOverwriteConfig)
                {
                    return;
                }

                if (versionToSave != _saveVersion)
                {
                    RestartSaveTimerForDueTime();
                    return;
                }

                if (json != null && TryWriteSerializedConfig(json))
                {
                    _pendingSave = false;
                    _automaticSaveRetryCount = 0;
                }
                else
                {
                    ScheduleSaveRetry();
                }
            }
        }

        private void ScheduleSaveRetry()
        {
            if (_automaticSaveRetryCount >= MaxAutomaticSaveRetries)
            {
                _saveTimer.Stop();
                Logger.Log(
                    "Konfiguration konnte nach mehreren Versuchen nicht gespeichert werden; " +
                    "ein neuer Versuch erfolgt bei der nächsten Änderung.");
                return;
            }

            _automaticSaveRetryCount++;
            double retryDelayMs = _saveDebounceMs * Math.Pow(
                SaveRetryBackoffFactor,
                _automaticSaveRetryCount - 1);
            ScheduleSaveTimer(retryDelayMs);
        }

        private void ScheduleSaveTimer(double delayMs)
        {
            _saveNotBeforeUtc = _utcNow().AddMilliseconds(delayMs);
            StartSaveTimer(delayMs);
        }

        private void RestartSaveTimerForDueTime()
        {
            double remainingDelayMs = Math.Max(
                1,
                (_saveNotBeforeUtc - _utcNow()).TotalMilliseconds);
            StartSaveTimer(remainingDelayMs);
        }

        private void StartSaveTimer(double delayMs)
        {
            _saveTimer.Stop();
            _saveTimer.Interval = Math.Max(1, delayMs);
            _saveTimer.Start();
        }

        internal bool IsSaveTimerEnabled => _saveTimer.Enabled;

        internal int AutomaticSaveRetryCount => _automaticSaveRetryCount;

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
            lock (_saveSync)
            {
                if (_disposed)
                {
                    return;
                }

                _saveTimer.Stop();
                if (_pendingSave && _canOverwriteConfig)
                {
                    string? json = TrySerializeConfig(_config);
                    if (json != null && TryWriteSerializedConfig(json))
                    {
                        _pendingSave = false;
                    }
                }

                _disposed = true;
                _saveTimer.Dispose();
            }
        }

        private bool TryBackupInvalidConfig()
        {
            try
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                string backupPath = $"{_configPath}.invalid-{timestamp}.bak";
                File.Copy(_configPath, backupPath, overwrite: false);
                Logger.Log($"Ungültige Konfiguration wurde gesichert: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "Ungültige Konfiguration konnte nicht gesichert werden; automatisches Überschreiben wurde deaktiviert",
                    ex);
                return false;
            }
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

            config.UISettings.LibraryFilter = LibraryFilterService.NormalizeFilterKey(config.UISettings.LibraryFilter);
        }

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
