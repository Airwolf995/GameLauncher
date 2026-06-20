using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GameLauncher.Core;
using GameLauncher.Services.Localization;

namespace GameLauncher.Models
{
    public class Game : ObservableObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = ""; // Steam, Xbox, Battle.net, etc.

        [JsonPropertyName("path")]
        public string Path { get; set; } = ""; // Path to exe or URI

        [JsonPropertyName("args")]
        public string Args { get; set; } = "";

        [JsonPropertyName("install_directory")]
        public string InstallDirectory { get; set; } = ""; // Directory to monitor for playtime

        [JsonPropertyName("executable_name")]
        public string ExecutableName { get; set; } = ""; // Specific process name to track (optional, high priority)

        [JsonPropertyName("source")]
        public string Source { get; set; } = ""; // e.g. "Steam", "Manual"

        [JsonPropertyName("launch_type")]
        public string LaunchType { get; set; } = "exe"; // "exe" or "uri"

        [JsonPropertyName("is_manual")]
        public bool IsManual { get; set; } = false;

        private string _imageUrl = "";
        private string? _resolvedImageUrl;

        [JsonPropertyName("image_url")]
        public string ImageUrl 
        { 
            get => _resolvedImageUrl ??= ResolveImageUrl(_imageUrl);
            set
            {
                if (string.Equals(_imageUrl, value, StringComparison.Ordinal))
                {
                    return;
                }

                _imageUrl = value;
                _resolvedImageUrl = null; // Invalidate cache
                OnPropertyChanged();
            }
        }

        private static string ResolveImageUrl(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return raw;

            if (!System.IO.Path.IsPathRooted(raw))
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, raw);

            return raw;
        }
        
        // UI Helper properties that might be ignored in JSON but useful for binding
        [JsonIgnore]
        public string DisplayName => Name;

        [JsonIgnore]
        public string LocalizedDescription =>
            string.IsNullOrWhiteSpace(Description)
                ? LocalizationService.Instance.Get("Details.NoDescription")
                : Description;

        private bool _isFavorite;
        [JsonIgnore]
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        private bool _isHidden;
        [JsonIgnore]
        public bool IsHidden
        {
            get => _isHidden;
            set => SetProperty(ref _isHidden, value);
        }

        private List<string> _tags = new List<string>();
        [JsonIgnore]
        public List<string> Tags
        {
            get => _tags;
            set => SetProperty(ref _tags, value ?? new List<string>());
        }

        private string _description = "";
        private string _publisher = "";
        private string _developer = "";
        private string _releaseDate = "";
        private List<string> _genres = new List<string>();

        // Extended Metadata
        [JsonPropertyName("description")]
        public string Description
        {
            get => _description;
            set
            {
                if (SetProperty(ref _description, value ?? ""))
                {
                    OnPropertyChanged(nameof(LocalizedDescription));
                }
            }
        }

        [JsonPropertyName("publisher")]
        public string Publisher
        {
            get => _publisher;
            set => SetProperty(ref _publisher, value ?? "");
        }

        [JsonPropertyName("developer")]
        public string Developer
        {
            get => _developer;
            set => SetProperty(ref _developer, value ?? "");
        }

        [JsonPropertyName("release_date")]
        public string ReleaseDate
        {
            get => _releaseDate;
            set => SetProperty(ref _releaseDate, value ?? "");
        }

        [JsonPropertyName("genres")]
        public List<string> Genres
        {
            get => _genres;
            set => SetProperty(ref _genres, value ?? new List<string>());
        }

        private int _playTime;
        private string? _cachedDisplayPlayTime;

        /// <summary>
        /// Play time in seconds.
        /// </summary>
        [JsonIgnore]
        public int PlayTime
        {
            get => _playTime;
            set 
            { 
                if (_playTime != value) 
                { 
                    _playTime = value;
                    _cachedDisplayPlayTime = null;
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(DisplayPlayTime));
                } 
            }
        }

        private DateTime? _lastPlayed;
        [JsonIgnore]
        public DateTime? LastPlayed
        {
            get => _lastPlayed;
            set
            {
                if (_lastPlayed != value)
                {
                    _lastPlayed = value;
                    _cachedDisplayPlayTime = null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayPlayTime));
                }
            }
        }

        [JsonIgnore]
        public string DisplayPlayTime => _cachedDisplayPlayTime ??= FormatPlayTime();

        private string FormatPlayTime()
        {
            var localization = LocalizationService.Instance;

            if (_playTime > 0)
            {
                int totalMinutes = _playTime / 60;
                int hours = totalMinutes / 60;
                int minutes = totalMinutes % 60;
                int seconds = _playTime % 60;

                if (hours > 0)
                    return localization.CurrentLanguage == AppLanguage.German
                        ? $"{hours} Std. {minutes} Min."
                        : $"{hours} hr {minutes} min";
                if (minutes > 0)
                    return localization.CurrentLanguage == AppLanguage.German
                        ? $"{minutes} Min. {seconds} Sek."
                        : $"{minutes} min {seconds} sec";
                return localization.CurrentLanguage == AppLanguage.German
                    ? $"{seconds} Sek."
                    : $"{seconds} sec";
            }

            if (_lastPlayed != null)
            {
                return localization.CurrentLanguage == AppLanguage.German
                    ? "Gespielt (< 30 Sek.)"
                    : "Played (< 30 sec)";
            }

            return localization.Get("Details.NeverPlayed");
        }

        public void RefreshLocalizedProperties()
        {
            _cachedDisplayPlayTime = null;
            OnPropertyChanged(nameof(DisplayPlayTime));
            OnPropertyChanged(nameof(LocalizedDescription));
        }

        public void RefreshMetadataProperties()
        {
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(LocalizedDescription));
            OnPropertyChanged(nameof(ReleaseDate));
            OnPropertyChanged(nameof(Developer));
            OnPropertyChanged(nameof(Publisher));
            OnPropertyChanged(nameof(Genres));
        }
    }
}
