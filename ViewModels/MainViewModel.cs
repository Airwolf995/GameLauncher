using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services;
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
        private CancellationTokenSource? _gamesViewRefreshCts;
        private readonly CancellationTokenSource _cts = new();
        private ObservableCollection<LocalizedOption> _filterOptions = [];
        private ObservableCollection<KeyValuePair<GameSortMode, string>> _sortOptions = [];
        private ObservableCollection<GameRow> _cardRows = [];
        private string _statusText = "";
        private int _gamesViewRefreshVersion;
        private int _cardColumns = 1;

        public MainViewModel(GameManager gameManager)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _localization = LocalizationService.Instance;
            _games = [];

            LoadLibraryViewSettings();
            _gamesView = new ListCollectionView(Array.Empty<Game>());
            RebuildSortOptions();
            PopulateFilterOptions();
            RefreshStatusText();

            LoadGamesCommand = new RelayCommand(async _ => await LoadGamesAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadGamesAsync(includeDeferredStartupGames: true));

            _gameManager.GamesUpdated += OnGamesUpdated;
            _localization.LanguageChanged += OnLanguageChanged;
        }

        public event EventHandler<LibraryViewRefreshedEventArgs>? LibraryViewRefreshed;

        public sealed class LibraryViewRefreshedEventArgs : EventArgs
        {
            public LibraryViewRefreshedEventArgs(IReadOnlyList<string> initialWarmupImagePaths)
            {
                InitialWarmupImagePaths = initialWarmupImagePaths;
            }

            public IReadOnlyList<string> InitialWarmupImagePaths { get; }
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

        public ObservableCollection<GameRow> CardRows
        {
            get => _cardRows;
            set => SetProperty(ref _cardRows, value);
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
                    _ = RefreshGamesViewAsync();
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
                SaveLibraryViewSettings();
                _ = RefreshGamesViewAsync();
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

                SaveLibraryViewSettings();
                _ = RefreshGamesViewAsync();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ICommand LoadGamesCommand { get; }
        public ICommand RefreshCommand { get; }

        public async Task LoadGamesAsync(
            bool loadSteamMetadataInBackground = true,
            bool includeDeferredStartupGames = false)
        {
            StatusText = _localization.Get("Main.StatusLoadingGames");
            try
            {
                var games = await _gameManager.LoadAllGamesAsync(loadSteamMetadataInBackground, _cts.Token);
                if (includeDeferredStartupGames)
                {
                    var deferredGames = await _gameManager.LoadDeferredStartupGamesAsync(_cts.Token);
                    if (deferredGames.Count > 0)
                    {
                        games = games
                            .Concat(deferredGames)
                            .GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .ToList();
                    }
                }

                new Action(() =>
                {
                    _games.Clear();
                    foreach (var game in games)
                    {
                        _games.Add(game);
                        game.RefreshLocalizedProperties();
                    }

                    PopulateFilterOptions();
                }).RunOnUI();

                await RefreshGamesViewAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void RefreshStatusText()
        {
            UpdateStatusText();
        }

        public Task RebuildLibraryViewAsync(bool saveSettings = false) => RefreshGamesViewAsync(saveSettings);

        public Task RefreshSteamMetadataAsync() => RefreshSteamMetadataForCurrentLanguageAsync();

        public async Task MergeGamesAsync(IEnumerable<Game> games)
        {
            var incomingGames = games
                .Where(game => game != null)
                .GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (incomingGames.Count == 0)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existingIds = new HashSet<string>(_games.Select(game => game.Id), StringComparer.OrdinalIgnoreCase);
                int addedCount = 0;

                foreach (var game in incomingGames)
                {
                    if (!existingIds.Add(game.Id))
                    {
                        continue;
                    }

                    game.RefreshLocalizedProperties();
                    _games.Add(game);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    PopulateFilterOptions();
                }
            });

            await RefreshGamesViewAsync();
        }

        public bool UpdateCardColumns(int columns)
        {
            int normalizedColumns = Math.Max(1, columns);
            if (_cardColumns == normalizedColumns)
            {
                return false;
            }

            _cardColumns = normalizedColumns;
            RebuildCardRowsFromCurrentView();
            return true;
        }

        public void Dispose()
        {
            _metadataRefreshCts?.Cancel();
            _metadataRefreshCts?.Dispose();
            _gamesViewRefreshCts?.Cancel();
            _gamesViewRefreshCts?.Dispose();
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
            });

            _ = RefreshGamesViewAsync();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RebuildSortOptions();
            PopulateFilterOptions();

            foreach (var game in _games)
            {
                game.RefreshLocalizedProperties();
            }

            UpdateStatusText();
            OnPropertyChanged(nameof(SelectedFilter));
            _ = RefreshGamesViewAsync();
            _ = RefreshSteamMetadataForCurrentLanguageAsync();
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
                    _ = RefreshGamesViewAsync();
                }

                OnPropertyChanged(nameof(SelectedFilter));
            }).RunOnUI();
        }

        private async Task RefreshGamesViewAsync(bool saveSettings = false)
        {
            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var previousRefreshCts = Interlocked.Exchange(ref _gamesViewRefreshCts, refreshCts);
            previousRefreshCts?.Cancel();
            previousRefreshCts?.Dispose();
            CancellationToken refreshToken = refreshCts.Token;

            var totalWatch = Stopwatch.StartNew();
            int refreshVersion = Interlocked.Increment(ref _gamesViewRefreshVersion);
            string searchText = _searchText;
            string selectedFilter = _selectedFilter;
            GameSortMode selectedSort = _selectedSort;
            int cardColumns = _cardColumns;

            try
            {
                List<Game> gamesSnapshot = [];
                var snapshotWatch = Stopwatch.StartNew();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    gamesSnapshot = _games.ToList();
                });
                snapshotWatch.Stop();

                refreshToken.ThrowIfCancellationRequested();

                var filterWatch = Stopwatch.StartNew();
                var viewSnapshot = await Task.Run(
                    () => LibraryViewSnapshotBuilder.Create(gamesSnapshot, searchText, selectedFilter, selectedSort, cardColumns),
                    refreshToken);
                filterWatch.Stop();

                if (refreshToken.IsCancellationRequested || _cts.IsCancellationRequested || refreshVersion != _gamesViewRefreshVersion)
                {
                    return;
                }

                var swapWatch = Stopwatch.StartNew();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _gamesView = new ListCollectionView(viewSnapshot.Games);
                    ReplaceCardRows(viewSnapshot.CardRows);
                    OnPropertyChanged(nameof(GamesView));
                    OnPropertyChanged(nameof(IsSearchActive));
                    UpdateStatusText();

                    if (saveSettings)
                    {
                        SaveLibraryViewSettings();
                    }

                    LibraryViewRefreshed?.Invoke(
                        this,
                        new LibraryViewRefreshedEventArgs(viewSnapshot.InitialWarmupImagePaths));
                });
                swapWatch.Stop();
                totalWatch.Stop();

                Logger.Log(
                    $"Bibliotheksfilter aktualisiert: Filter={selectedFilter}, Suche='{searchText}', Sortierung={selectedSort}, Quelle={gamesSnapshot.Count}, Ergebnis={viewSnapshot.Games.Count}, Snapshot={snapshotWatch.ElapsedMilliseconds} ms, Filterung={filterWatch.ElapsedMilliseconds} ms, View-Swap={swapWatch.ElapsedMilliseconds} ms, Gesamt={totalWatch.ElapsedMilliseconds} ms.");
            }
            catch (OperationCanceledException) when (refreshToken.IsCancellationRequested || _cts.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _gamesViewRefreshCts, null, refreshCts), refreshCts))
                {
                    // Das aktuelle Refresh-Token wurde bereits per using entsorgt.
                }
            }
        }

        private bool FilterGames(Game game) =>
            LibraryViewSnapshotBuilder.MatchesFilter(game, _searchText, _selectedFilter);

        private void RebuildCardRowsFromCurrentView()
        {
            if (_gamesView.SourceCollection is not IEnumerable<Game> games)
            {
                ReplaceCardRows([]);
                return;
            }

            ReplaceCardRows(LibraryViewSnapshotBuilder.BuildCardRows(games.ToList(), _cardColumns));
        }

        private void ReplaceCardRows(IEnumerable<GameRow> rows)
        {
            CardRows = new ObservableCollection<GameRow>(rows);
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
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
