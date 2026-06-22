using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services
{
    internal sealed class SteamMetadataCacheService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(14);

        private readonly string _cachePath;
        private readonly object _lock = new();
        private readonly Dictionary<string, SteamMetadataCacheEntry> _entries;
        private readonly System.Timers.Timer _saveTimer;
        private bool _pendingSave;

        public SteamMetadataCacheService()
        {
            _cachePath = Path.Combine(AppPaths.GetDocumentsRoot(), "steam_metadata_cache.json");
            _entries = LoadEntries();
            _saveTimer = new System.Timers.Timer(1500)
            {
                AutoReset = false
            };
            _saveTimer.Elapsed += (_, _) =>
            {
                lock (_lock)
                {
                    if (_pendingSave)
                    {
                        SaveEntriesUnsafe();
                        _pendingSave = false;
                    }
                }
            };
        }

        public bool ApplyCachedMetadata(Game game, AppLanguage language)
        {
            if (!TryGetEntry(game, language, out var entry))
            {
                return false;
            }

            ApplyEntry(game, entry);
            return true;
        }

        public bool NeedsRefresh(Game game, AppLanguage language)
        {
            if (!TryGetEntry(game, language, out var entry))
            {
                return true;
            }

            if (DateTime.UtcNow - entry.UpdatedAtUtc > RefreshInterval)
            {
                return true;
            }

            return string.IsNullOrWhiteSpace(entry.Description) ||
                   string.IsNullOrWhiteSpace(entry.ReleaseDate) ||
                   string.IsNullOrWhiteSpace(entry.Developer) ||
                   string.IsNullOrWhiteSpace(entry.Publisher) ||
                   entry.Genres.Count == 0;
        }

        public void Update(Game game, AppLanguage language)
        {
            string? appId = GetSteamAppId(game);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return;
            }

            var entry = new SteamMetadataCacheEntry
            {
                AppId = appId,
                LanguageCode = LocalizationService.ToLanguageCode(language),
                UpdatedAtUtc = DateTime.UtcNow,
                Description = game.Description ?? "",
                ReleaseDate = game.ReleaseDate ?? "",
                Developer = game.Developer ?? "",
                Publisher = game.Publisher ?? "",
                Genres = game.Genres != null ? new List<string>(game.Genres) : []
            };

            lock (_lock)
            {
                _entries[BuildKey(appId, entry.LanguageCode)] = entry;
                _pendingSave = true;
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }

        public void Dispose()
        {
            _saveTimer.Stop();
            _saveTimer.Dispose();

            lock (_lock)
            {
                if (_pendingSave)
                {
                    SaveEntriesUnsafe();
                    _pendingSave = false;
                }
            }
        }

        private bool TryGetEntry(Game game, AppLanguage language, out SteamMetadataCacheEntry entry)
        {
            entry = null!;
            string? appId = GetSteamAppId(game);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return false;
            }

            string key = BuildKey(appId, LocalizationService.ToLanguageCode(language));
            lock (_lock)
            {
                return _entries.TryGetValue(key, out entry!);
            }
        }

        private static string? GetSteamAppId(Game game)
        {
            if (!string.Equals(game.Platform, Constants.Platforms.Steam, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(game.Id) ||
                !game.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return game.Id.Substring("steam:".Length);
        }

        private static string BuildKey(string appId, string languageCode) =>
            $"{appId}:{languageCode}";

        private static void ApplyEntry(Game game, SteamMetadataCacheEntry entry)
        {
            game.Description = entry.Description ?? "";
            game.ReleaseDate = entry.ReleaseDate ?? "";
            game.Developer = entry.Developer ?? "";
            game.Publisher = entry.Publisher ?? "";
            game.Genres = entry.Genres != null ? new List<string>(entry.Genres) : [];
            game.RefreshMetadataProperties();
        }

        private Dictionary<string, SteamMetadataCacheEntry> LoadEntries()
        {
            try
            {
                if (!File.Exists(_cachePath))
                {
                    return new Dictionary<string, SteamMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase);
                }

                string json = File.ReadAllText(_cachePath);
                var entries = JsonSerializer.Deserialize<List<SteamMetadataCacheEntry>>(json) ?? [];
                var result = new Dictionary<string, SteamMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.AppId) || string.IsNullOrWhiteSpace(entry.LanguageCode))
                    {
                        continue;
                    }

                    entry.Genres ??= [];
                    result[BuildKey(entry.AppId, entry.LanguageCode)] = entry;
                }

                Logger.Log($"Steam-Metadaten-Cache geladen: {result.Count} Einträge.");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("Steam-Metadaten-Cache konnte nicht geladen werden.", ex);
                return new Dictionary<string, SteamMetadataCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveEntriesUnsafe()
        {
            try
            {
                string directory = Path.GetDirectoryName(_cachePath) ?? AppPaths.GetDocumentsRoot();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(_entries.Values, JsonOptions);
                string tempPath = _cachePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _cachePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error("Steam-Metadaten-Cache konnte nicht gespeichert werden.", ex);
            }
        }

        private sealed class SteamMetadataCacheEntry
        {
            [JsonPropertyName("app_id")]
            public string AppId { get; set; } = "";

            [JsonPropertyName("language_code")]
            public string LanguageCode { get; set; } = "";

            [JsonPropertyName("updated_at_utc")]
            public DateTime UpdatedAtUtc { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; } = "";

            [JsonPropertyName("release_date")]
            public string ReleaseDate { get; set; } = "";

            [JsonPropertyName("developer")]
            public string Developer { get; set; } = "";

            [JsonPropertyName("publisher")]
            public string Publisher { get; set; } = "";

            [JsonPropertyName("genres")]
            public List<string> Genres { get; set; } = [];
        }
    }
}
