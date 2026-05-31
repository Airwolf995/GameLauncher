using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private const string TagDisplayPrefix = "🏷️ ";

        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization;
        private readonly ObservableCollection<Game> _games;
        private ICollectionView _gamesView = null!;
        private string _searchText = "";
        private string _selectedFilter = Constants.Filters.All;
        private string _preferredFilter = Constants.Filters.All;
        private GameSortMode _selectedSort = GameSortMode.Name;
        private Timer? _filterDebounce;
        private CancellationTokenSource? _metadataRefreshCts;
        private readonly CancellationTokenSource _cts = new();
        private ObservableCollection<LocalizedOption> _filterOptions = [];
        private ObservableCollection<KeyValuePair<GameSortMode, string>> _sortOptions = [];
        private string _statusText = "";

        public MainViewModel(GameManager gameManager)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _localization = LocalizationService.Instance;
            _games = [];

            LoadLibraryViewSettings();
            ConfigureGamesView();
            RebuildSortOptions();
            PopulateFilterOptions();
            RefreshStatusText();

            LoadGamesCommand = new RelayCommand(async _ => await LoadGamesAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadGamesAsync());

            _gameManager.GamesUpdated += OnGamesUpdated;
            _localization.LanguageChanged += OnLanguageChanged;
        }

        public ICollectionView GamesView => _gamesView;
        public ObservableCollection<Game> Games => _games;

        public ObservableCollection<LocalizedOption> FilterOptions
        {
            get => _filterOptions;
            set => SetProperty(ref _filterOptions, value);
        }

        public ObservableCollection<KeyValuePair<GameSortMode, string>> SortOptions
        {
            get => _sortOptions;
            set => SetProperty(ref _sortOptions, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value))
                {
                    return;
                }

                _filterDebounce?.Dispose();
                _filterDebounce = new Timer(_ =>
                {
                    new Action(() =>
                    {
                        _gamesView.Refresh();
                        OnPropertyChanged(nameof(IsSearchActive));
                    }).RunOnUI();
                }, null, 150, Timeout.Infinite);
            }
        }

        public bool IsSearchActive => !string.IsNullOrEmpty(SearchText);

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                var normalized = NormalizeFilterKey(value);
                if (!SetProperty(ref _selectedFilter, normalized))
                {
                    return;
                }

                _preferredFilter = normalized;
                _gamesView.Refresh();
                UpdateStatusText();
                SaveLibraryViewSettings();
            }
        }

        public GameSortMode SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (!SetProperty(ref _selectedSort, value))
                {
                    return;
                }

                ApplySort();
                SaveLibraryViewSettings();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ICommand LoadGamesCommand { get; }
        public ICommand RefreshCommand { get; }

        public async Task LoadGamesAsync()
        {
            StatusText = _localization.Get("Main.StatusLoadingGames");
            try
            {
                var games = await _gameManager.LoadAllGamesAsync(_cts.Token);

                new Action(() =>
                {
                    _games.Clear();
                    foreach (var game in games)
                    {
                        _games.Add(game);
                        game.RefreshLocalizedProperties();
                    }

                    ConfigureGamesView();
                    PopulateFilterOptions();

                    OnPropertyChanged(nameof(GamesView));
                    OnPropertyChanged(nameof(IsSearchActive));
                    UpdateStatusText();
                }).RunOnUI();
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void RefreshStatusText()
        {
            UpdateStatusText();
        }

        public void Dispose()
        {
            _metadataRefreshCts?.Cancel();
            _metadataRefreshCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
            _filterDebounce?.Dispose();
            _gameManager.GamesUpdated -= OnGamesUpdated;
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        private void OnGamesUpdated(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PopulateFilterOptions();
                _gamesView.Refresh();
                UpdateStatusText();
            });
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RebuildSortOptions();
            PopulateFilterOptions();

            foreach (var game in _games)
            {
                game.RefreshLocalizedProperties();
            }

            _gamesView.Refresh();
            UpdateStatusText();
            OnPropertyChanged(nameof(SelectedFilter));
            _ = RefreshSteamMetadataForCurrentLanguageAsync();
        }

        private bool FilterGames(object item)
        {
            if (item is not Game game)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                bool matchesName = game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
                bool matchesTag = game.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

                if (!matchesName && !matchesTag)
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(_selectedFilter) || _selectedFilter == Constants.Filters.All)
            {
                return !game.IsHidden;
            }

            if (_selectedFilter == Constants.Filters.Hidden)
            {
                return game.IsHidden;
            }

            if (game.IsHidden)
            {
                return false;
            }

            return _selectedFilter switch
            {
                Constants.Filters.Favorites => game.IsFavorite,
                Constants.Filters.Manual => game.IsManual,
                Constants.Platforms.Steam => game.Platform == Constants.Platforms.Steam,
                Constants.Platforms.Epic => game.Platform == Constants.Platforms.Epic,
                "Ubisoft Connect" => game.Platform == "Ubisoft Connect",
                "EA App" => game.Platform == "EA App",
                "Xbox" => game.Platform == "Xbox",
                Constants.Platforms.GOG => game.Platform == Constants.Platforms.GOG,
                _ when _selectedFilter.StartsWith(Constants.Filters.TagPrefix, StringComparison.Ordinal) =>
                    game.Tags.Contains(_selectedFilter.Substring(Constants.Filters.TagPrefix.Length)),
                _ => true
            };
        }

        private void ApplySort()
        {
            _gamesView.SortDescriptions.Clear();
            ConfigureLiveSorting();

            switch (_selectedSort)
            {
                case GameSortMode.Name:
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Name), ListSortDirection.Ascending));
                    break;
                case GameSortMode.Favorites:
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.IsFavorite), ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Name), ListSortDirection.Ascending));
                    break;
                case GameSortMode.LastPlayed:
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.LastPlayed), ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Name), ListSortDirection.Ascending));
                    break;
                case GameSortMode.PlayTime:
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.PlayTime), ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Name), ListSortDirection.Ascending));
                    break;
            }
        }

        private void ConfigureGamesView()
        {
            _gamesView = CollectionViewSource.GetDefaultView(_games);
            _gamesView.Filter = FilterGames;
            ApplySort();
        }

        private void ConfigureLiveSorting()
        {
            if (_gamesView is not ICollectionViewLiveShaping liveShaping)
            {
                return;
            }

            if (liveShaping.CanChangeLiveSorting)
            {
                liveShaping.IsLiveSorting = true;
            }

            liveShaping.LiveSortingProperties.Clear();

            switch (_selectedSort)
            {
                case GameSortMode.Name:
                    liveShaping.LiveSortingProperties.Add(nameof(Game.Name));
                    break;
                case GameSortMode.Favorites:
                    liveShaping.LiveSortingProperties.Add(nameof(Game.IsFavorite));
                    liveShaping.LiveSortingProperties.Add(nameof(Game.Name));
                    break;
                case GameSortMode.LastPlayed:
                    liveShaping.LiveSortingProperties.Add(nameof(Game.LastPlayed));
                    liveShaping.LiveSortingProperties.Add(nameof(Game.Name));
                    break;
                case GameSortMode.PlayTime:
                    liveShaping.LiveSortingProperties.Add(nameof(Game.PlayTime));
                    liveShaping.LiveSortingProperties.Add(nameof(Game.Name));
                    break;
            }
        }

        private void LoadLibraryViewSettings()
        {
            var uiSettings = _gameManager.GetConfig().UISettings;
            _selectedSort = uiSettings.LibrarySortMode;
            _selectedFilter = NormalizeFilterKey(uiSettings.LibraryFilter);
            _preferredFilter = _selectedFilter;
        }

        private void SaveLibraryViewSettings()
        {
            var uiSettings = _gameManager.GetConfig().UISettings;
            uiSettings.LibrarySortMode = _selectedSort;
            uiSettings.LibraryFilter = _preferredFilter;
            _gameManager.SaveConfig();
        }

        private void UpdateStatusText()
        {
            int steam = 0, gog = 0, epic = 0, ubi = 0, ea = 0, xbox = 0, manual = 0;
            foreach (var game in _games)
            {
                if (game.IsManual) manual++;
                else if (game.Platform == Constants.Platforms.Steam) steam++;
                else if (game.Platform == Constants.Platforms.GOG) gog++;
                else if (game.Platform == Constants.Platforms.Epic) epic++;
                else if (game.Platform == "Ubisoft Connect") ubi++;
                else if (game.Platform == "EA App") ea++;
                else if (game.Platform == "Xbox") xbox++;
            }

            StatusText = _localization.Format("Main.StatusSummary", _games.Count, steam, gog, epic, ubi, ea, xbox, manual);
        }

        private void PopulateFilterOptions()
        {
            new Action(() =>
            {
                var desiredFilter = string.IsNullOrWhiteSpace(_preferredFilter) ? Constants.Filters.All : _preferredFilter;
                var newOptions = new List<LocalizedOption>
                {
                    CreateOption(Constants.Filters.All, _localization.Get("Filter.All")),
                    CreateOption(Constants.Platforms.Steam, Constants.Platforms.Steam),
                    CreateOption(Constants.Platforms.Epic, Constants.Platforms.Epic),
                    CreateOption(Constants.Platforms.GOG, Constants.Platforms.GOG),
                    CreateOption("Ubisoft Connect", "Ubisoft Connect"),
                    CreateOption("EA App", "EA App"),
                    CreateOption("Xbox", "Xbox"),
                    CreateOption(Constants.Filters.Manual, _localization.Get("Filter.Manual")),
                    CreateOption(Constants.Filters.Hidden, _localization.Get("Filter.Hidden"))
                };

                var usedTags = _gameManager.GetAllUsedTags();
                if (usedTags.Count > 0 || Constants.Tags.DefaultTags.Length > 0)
                {
                    newOptions.Add(new LocalizedOption { Key = "__separator__", DisplayName = "──────────", IsSeparator = true });
                }

                foreach (var tag in Constants.Tags.DefaultTags)
                {
                    newOptions.Add(CreateTagOption(tag));
                }

                foreach (var tag in usedTags)
                {
                    if (!Constants.Tags.DefaultTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        newOptions.Add(CreateTagOption(tag));
                    }
                }

                ReplaceFilterOptionsPreservingItems(newOptions);

                var activeFilter = FilterOptions.Any(option => option.Key == desiredFilter)
                    ? desiredFilter
                    : Constants.Filters.All;

                if (_selectedFilter != activeFilter)
                {
                    _selectedFilter = activeFilter;
                    _gamesView.Refresh();
                    UpdateStatusText();
                }

                OnPropertyChanged(nameof(SelectedFilter));
            }).RunOnUI();
        }

        private void RebuildSortOptions()
        {
            SortOptions = new ObservableCollection<KeyValuePair<GameSortMode, string>>
            {
                KeyValuePair.Create(GameSortMode.Name, _localization.Get("Sort.Name")),
                KeyValuePair.Create(GameSortMode.Favorites, _localization.Get("Sort.Favorites")),
                KeyValuePair.Create(GameSortMode.LastPlayed, _localization.Get("Sort.LastPlayed")),
                KeyValuePair.Create(GameSortMode.PlayTime, _localization.Get("Sort.PlayTime"))
            };
        }

        private LocalizedOption CreateTagOption(string tag) =>
            new()
            {
                Key = $"{Constants.Filters.TagPrefix}{tag}",
                DisplayName = string.Format(_localization.CurrentCulture, _localization.Get("Filter.TagPrefix"), tag)
            };

        private static LocalizedOption CreateOption(string key, string displayName) =>
            new()
            {
                Key = key,
                DisplayName = displayName
            };

        private void ReplaceFilterOptionsPreservingItems(IReadOnlyList<LocalizedOption> newOptions)
        {
            for (int targetIndex = 0; targetIndex < newOptions.Count; targetIndex++)
            {
                var newOption = newOptions[targetIndex];
                int existingIndex = IndexOfFilterOption(newOption.Key);

                if (existingIndex >= 0)
                {
                    var existingOption = FilterOptions[existingIndex];
                    existingOption.DisplayName = newOption.DisplayName;

                    if (existingIndex != targetIndex)
                    {
                        FilterOptions.Move(existingIndex, targetIndex);
                    }
                }
                else
                {
                    FilterOptions.Insert(targetIndex, newOption);
                }
            }

            while (FilterOptions.Count > newOptions.Count)
            {
                FilterOptions.RemoveAt(FilterOptions.Count - 1);
            }
        }

        private int IndexOfFilterOption(string key)
        {
            for (int i = 0; i < FilterOptions.Count; i++)
            {
                if (FilterOptions[i].Key == key)
                {
                    return i;
                }
            }

            return -1;
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
            _ when filter.StartsWith(TagDisplayPrefix, StringComparison.Ordinal) => $"{Constants.Filters.TagPrefix}{filter.Substring(TagDisplayPrefix.Length)}",
            _ => filter
        };

        private async Task RefreshSteamMetadataForCurrentLanguageAsync()
        {
            _metadataRefreshCts?.Cancel();
            _metadataRefreshCts?.Dispose();
            _metadataRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var token = _metadataRefreshCts.Token;

            try
            {
                await _gameManager.RefreshSteamMetadataAsync(_games.ToList(), token);
                if (!token.IsCancellationRequested)
                {
                    new Action(() => _gamesView.Refresh()).RunOnUI();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
