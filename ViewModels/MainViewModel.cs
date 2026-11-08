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
using System.Windows.Threading;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization;
        private readonly ObservableCollection<Game> _games;
        private ICollectionView _gamesView = null!;
        private string _searchText = "";
        private string _selectedFilter = Constants.Filters.All;
        private string _preferredFilter = Constants.Filters.All;
        private GameSortMode _selectedSort = GameSortMode.Name;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _filterDebounce;
        private readonly Func<List<Game>, string, string, GameSortMode, int, LibraryViewSnapshot> _buildSnapshot;
        private bool _disposed;
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
            : this(gameManager, LibraryViewSnapshotBuilder.Create)
        {
        }

        // Steuerbarer Hintergrundaufbau für Tests konkurrierender Aktualisierungen.
        internal MainViewModel(GameManager gameManager,
            Func<List<Game>, string, string, GameSortMode, int, LibraryViewSnapshot> buildSnapshot)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _buildSnapshot = buildSnapshot;
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _filterDebounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _filterDebounce.Tick += OnFilterDebounce;
            _localization = LocalizationService.Instance;
            _games = [];

            LoadLibraryViewSettings();
            _gamesView = new ListCollectionView(Array.Empty<Game>());
            RebuildSortOptions();
            PopulateFilterOptions();
            RefreshStatusText();


            _gameManager.GamesUpdated += OnGamesUpdated;
            _localization.LanguageChanged += OnLanguageChanged;
        }

        public event EventHandler? LibraryViewRefreshed;

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

                if (_disposed) return;
                // Auch während der Wartezeit darf ein altes Ergebnis nicht mehr landen.
                _gamesViewRefreshVersion++;
                _filterDebounce.Stop();
                _filterDebounce.Start();
            }
        }

        public bool IsSearchActive => !string.IsNullOrEmpty(SearchText);

        private void OnFilterDebounce(object? sender, EventArgs e)
        {
            _filterDebounce.Stop();
            _ = RefreshGamesViewAsync();
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                var normalized = LibraryFilterService.NormalizeFilterKey(value);
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

        public async Task LoadGamesAsync(
            bool loadSteamMetadataInBackground = true,
            bool includeDeferredStartupGames = false)
        {
            if (_disposed) return;
            var token = _cts.Token;
            StatusText = _localization.Get("Main.StatusLoadingGames");
            try
            {
                var games = await _gameManager.LoadAllGamesAsync(token);
                token.ThrowIfCancellationRequested();
                if (includeDeferredStartupGames)
                {
                    var deferredGames = await _gameManager.LoadDeferredStartupGamesAsync(token);
                    token.ThrowIfCancellationRequested();
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

                if (loadSteamMetadataInBackground)
                {
                    // Derselbe Weg wie beim Sprachwechsel: Ein weiterer Wechsel bricht
                    // den Abruf ab, sodass keine Texte unter der falschen Sprache landen.
                    _ = RefreshSteamMetadataForCurrentLanguageAsync();
                }

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

        public Task RebuildLibraryViewAsync() => RefreshGamesViewAsync();

        public Task RefreshSteamMetadataAsync() => RefreshSteamMetadataForCurrentLanguageAsync();

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
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(Dispose);
                return;
            }
            if (_disposed) return;
            _disposed = true;
            _filterDebounce.Stop();
            _filterDebounce.Tick -= OnFilterDebounce;
            _metadataRefreshCts?.Cancel();
            _gamesViewRefreshCts?.Cancel();
            _cts.Cancel();
            _cts.Dispose();
            _gameManager.GamesUpdated -= OnGamesUpdated;
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        private void OnGamesUpdated(object? sender, EventArgs e)
        {
            PopulateFilterOptions();
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
            var settings = _gameManager.ReadConfig(config => (
                config.UISettings.LibrarySortMode,
                config.UISettings.LibraryFilter));
            _selectedSort = settings.LibrarySortMode;
            _selectedFilter = LibraryFilterService.NormalizeFilterKey(settings.LibraryFilter);
            _preferredFilter = _selectedFilter;
        }

        private void SaveLibraryViewSettings()
        {
            _gameManager.UpdateConfig(config =>
            {
                config.UISettings.LibrarySortMode = _selectedSort;
                config.UISettings.LibraryFilter = _preferredFilter;
            });
            _gameManager.SaveConfig();
        }

        private void UpdateStatusText()
        {
            int steam = 0, gog = 0, epic = 0, ubi = 0, ea = 0, xbox = 0, battleNet = 0, manual = 0;
            foreach (var game in _games)
            {
                if (game.IsManual) manual++;
                else if (game.Platform == Constants.Platforms.Steam) steam++;
                else if (game.Platform == Constants.Platforms.GOG) gog++;
                else if (game.Platform == Constants.Platforms.Epic) epic++;
                else if (game.Platform == Constants.Platforms.UbisoftConnect) ubi++;
                else if (game.Platform == Constants.Platforms.EAApp) ea++;
                else if (game.Platform == Constants.Platforms.Xbox) xbox++;
                else if (game.Platform == Constants.Platforms.BattleNet) battleNet++;
            }

            string summary = _localization.Format("Main.StatusSummary", _games.Count, steam, gog, epic, ubi, ea, xbox, battleNet, manual);

            // Fehlgeschlagene Plattformen benennen, damit eine unvollständige
            // Bibliothek nicht wie ein normales Ergebnis aussieht.
            var failedPlatforms = _gameManager.LastScanFailures;
            if (failedPlatforms.Count > 0)
            {
                summary += "  " + _localization.Format(
                    "Main.StatusScanFailed",
                    string.Join(", ", failedPlatforms));
            }

            StatusText = summary;
        }

        private void PopulateFilterOptions()
        {
            new Action(() =>
            {
                var desiredFilter = string.IsNullOrWhiteSpace(_preferredFilter) ? Constants.Filters.All : _preferredFilter;
                var usedTags = _gameManager.GetAllUsedTags();
                var newOptions = LibraryFilterOptionsBuilder.Build(_localization, usedTags.ToList());

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

        private async Task RefreshGamesViewAsync()
        {
            if (!_dispatcher.CheckAccess())
            {
                try { await _dispatcher.InvokeAsync(RefreshGamesViewAsync).Task.Unwrap(); }
                catch (OperationCanceledException) when (_dispatcher.HasShutdownStarted) { }
                catch (Exception ex) { Logger.Error("Bibliotheksaktualisierung konnte nicht eingeplant werden", ex); }
                return;
            }
            if (_disposed) return;

            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var previousRefreshCts = _gamesViewRefreshCts;
            _gamesViewRefreshCts = refreshCts;
            previousRefreshCts?.Cancel();
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
                await _dispatcher.InvokeAsync(() =>
                {
                    gamesSnapshot = _games.ToList();
                });
                snapshotWatch.Stop();

                refreshToken.ThrowIfCancellationRequested();

                var filterWatch = Stopwatch.StartNew();
                var viewSnapshot = await Task.Run(
                    () => _buildSnapshot(gamesSnapshot, searchText, selectedFilter, selectedSort, cardColumns),
                    refreshToken);
                filterWatch.Stop();

                if (refreshToken.IsCancellationRequested || _cts.IsCancellationRequested || refreshVersion != _gamesViewRefreshVersion)
                {
                    return;
                }

                var swapWatch = Stopwatch.StartNew();
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || refreshToken.IsCancellationRequested || refreshVersion != _gamesViewRefreshVersion)
                    {
                        return;
                    }
                    _gamesView = new ListCollectionView(viewSnapshot.Games);
                    ReplaceCardRows(cardColumns == _cardColumns
                        ? viewSnapshot.CardRows
                        : LibraryViewSnapshotBuilder.BuildCardRows(viewSnapshot.Games, _cardColumns));
                    OnPropertyChanged(nameof(GamesView));
                    OnPropertyChanged(nameof(IsSearchActive));
                    UpdateStatusText();

                    LibraryViewRefreshed?.Invoke(this, EventArgs.Empty);
                });
                swapWatch.Stop();
                totalWatch.Stop();

                Logger.Log(
                    $"Bibliotheksfilter aktualisiert: Filter={selectedFilter}, Suche='{searchText}', Sortierung={selectedSort}, Quelle={gamesSnapshot.Count}, Ergebnis={viewSnapshot.Games.Count}, Snapshot={snapshotWatch.ElapsedMilliseconds} ms, Filterung={filterWatch.ElapsedMilliseconds} ms, View-Swap={swapWatch.ElapsedMilliseconds} ms, Gesamt={totalWatch.ElapsedMilliseconds} ms.");
            }
            catch (OperationCanceledException) when (refreshToken.IsCancellationRequested || _cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Bibliotheksansicht konnte nicht aktualisiert werden", ex);
            }
            finally
            {
                // Jeder Durchlauf entsorgt seine Quelle selbst. Andere brechen nur ab.
                if (ReferenceEquals(_gamesViewRefreshCts, refreshCts))
                {
                    _gamesViewRefreshCts = null;
                }
            }
        }

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

        private async Task RefreshSteamMetadataForCurrentLanguageAsync()
        {
            if (!_dispatcher.CheckAccess())
            {
                try { await _dispatcher.InvokeAsync(RefreshSteamMetadataForCurrentLanguageAsync).Task.Unwrap(); }
                catch (OperationCanceledException) when (_dispatcher.HasShutdownStarted) { }
                catch (Exception ex) { Logger.Error("Steam-Metadatenaktualisierung konnte nicht eingeplant werden", ex); }
                return;
            }
            if (_disposed) return;

            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _metadataRefreshCts?.Cancel();
            _metadataRefreshCts = refreshCts;
            var token = refreshCts.Token;

            try
            {
                await _gameManager.RefreshSteamMetadataAsync(_games.ToList(), token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Steam-Metadaten konnten nicht aktualisiert werden", ex);
            }
            finally
            {
                if (ReferenceEquals(_metadataRefreshCts, refreshCts))
                {
                    _metadataRefreshCts = null;
                }
            }
        }
    }
}
