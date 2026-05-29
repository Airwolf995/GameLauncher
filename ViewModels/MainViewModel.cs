using System;
using System.Collections.Generic;
using System.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GameLauncher.Core;
using GameLauncher.Models;

namespace GameLauncher.ViewModels
{
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly GameManager _gameManager;
        private ObservableCollection<Game> _games;
        private ICollectionView _gamesView = null!;
        private string _searchText = "";
        private string _selectedFilter = "Alle";
        private GameSortMode _selectedSort = GameSortMode.Name;
        private System.Threading.Timer? _filterDebounce;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private ObservableCollection<string> _filterOptions = new()
        {
            "Alle", "Steam", "Epic Games", "GOG", "Ubisoft Connect", "EA App", "Xbox", "Manuell", "Ausgeblendet"
        };
        
        public ObservableCollection<string> FilterOptions
        {
            get => _filterOptions;
            set => SetProperty(ref _filterOptions, value);
        }

        public MainViewModel(GameManager gameManager)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _games = new ObservableCollection<Game>();
            
            ConfigureGamesView();

            // Commands
            LoadGamesCommand = new RelayCommand(async _ => await LoadGamesAsync());
            RefreshCommand = new RelayCommand(async _ => await LoadGamesAsync());

            // Listen to external updates
            _gameManager.GamesUpdated += OnGamesUpdated;

            PopulateFilterOptions();
        }

        public ICollectionView GamesView => _gamesView;
        public ObservableCollection<Game> Games => _games;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // Debounce: wait 150ms after last keystroke before filtering
                    _filterDebounce?.Dispose();
                    _filterDebounce = new System.Threading.Timer(_ =>
                    {
                        new Action(() =>
                        {
                            _gamesView.Refresh();
                            OnPropertyChanged(nameof(IsSearchActive));
                        }).RunOnUI();
                    }, null, 150, System.Threading.Timeout.Infinite);
                }
            }
        }

        public bool IsSearchActive => !string.IsNullOrEmpty(SearchText);

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                var val = value ?? "Alle";
                if (val == "──────────") return;
                if (SetProperty(ref _selectedFilter, val))
                {
                    _gamesView.Refresh();
                    UpdateStatusText();
                }
            }
        }

        /// <summary>
        /// Available sort options for XAML ComboBox binding.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<GameSortMode, string>> SortOptions { get; } = new[]
        {
            KeyValuePair.Create(GameSortMode.Name, "Name"),
            KeyValuePair.Create(GameSortMode.Favorites, "Favoriten"),
            KeyValuePair.Create(GameSortMode.LastPlayed, "Zuletzt gespielt"),
            KeyValuePair.Create(GameSortMode.PlayTime, "Spielzeit"),
        };

        public GameSortMode SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (SetProperty(ref _selectedSort, value))
                {
                    ApplySort();
                }
            }
        }

        private string _statusText = "Bereit";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ICommand LoadGamesCommand { get; }
        public ICommand RefreshCommand { get; }

        public async Task LoadGamesAsync()
        {
            StatusText = "Lade Spiele...";
            try 
            {
                var games = await _gameManager.LoadAllGamesAsync(_cts.Token);
                
                new Action(() =>
                {
                    // Clear and add to EXISTING collection (not replace) to keep external references valid
                    // This is important for PlayTimeService which holds a reference to this collection
                    _games.Clear();
                    foreach (var game in games)
                    {
                        _games.Add(game);
                    }
                    
                    ConfigureGamesView();

                    PopulateFilterOptions();

                    // Notify UI of property changes
                    OnPropertyChanged(nameof(GamesView));
                    OnPropertyChanged(nameof(IsSearchActive));
                    UpdateStatusText();
                }).RunOnUI();
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
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

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _filterDebounce?.Dispose();
            _gameManager.GamesUpdated -= OnGamesUpdated;
        }

        private bool FilterGames(object item)
        {
            if (item is not Game game) return false;

            // Text Filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                 if (!game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return false;
            }

            // Category Filter
            if (string.IsNullOrEmpty(_selectedFilter) || _selectedFilter == "Alle")
            {
                if (game.IsHidden) return false;
            }
            else if (_selectedFilter == "Ausgeblendet")
            {
                if (!game.IsHidden) return false;
            }
            else
            {
                if (game.IsHidden) return false;
                
                switch (_selectedFilter)
                {
                    case "Favoriten":
                        if (!game.IsFavorite) return false;
                        break;
                    case "Steam":
                        if (game.Platform != "Steam") return false;
                        break;
                    case "Epic Games":
                        if (game.Platform != "Epic Games") return false;
                        break;
                    case "Ubisoft Connect":
                        if (game.Platform != "Ubisoft Connect") return false;
                        break;
                    case "EA App":
                        if (game.Platform != "EA App") return false;
                        break;
                    case "Xbox":
                        if (game.Platform != "Xbox") return false;
                        break;
                    case "GOG":
                        if (game.Platform != "GOG") return false;
                        break;
                    case "Manuell":
                        if (!game.IsManual) return false;
                        break;
                    default:
                        // Tag filter - check if the filter matches a tag
                        if (_selectedFilter.StartsWith("🏷️ "))
                        {
                            string tagName = _selectedFilter.Substring(4); // Remove "🏷️ " prefix
                            if (!game.Tags.Contains(tagName)) return false;
                        }
                        break;
                }
            }

            return true;
        }

        private void ApplySort()
        {
            _gamesView.SortDescriptions.Clear();
            ConfigureLiveSorting();

            switch (_selectedSort)
            {
                case GameSortMode.Name:
                    _gamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                    break;
                case GameSortMode.Favorites:
                    _gamesView.SortDescriptions.Add(new SortDescription("IsFavorite", ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                    break;
                case GameSortMode.LastPlayed:
                    _gamesView.SortDescriptions.Add(new SortDescription("LastPlayed", ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                    break;
                case GameSortMode.PlayTime:
                    _gamesView.SortDescriptions.Add(new SortDescription("PlayTime", ListSortDirection.Descending));
                    _gamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
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



        private void UpdateStatusText()
        {
            int steam = 0, gog = 0, epic = 0, ubi = 0, ea = 0, xbox = 0, manual = 0;
            foreach (var game in _games)
            {
                if (game.IsManual) manual++;
                else if (game.Platform == "Steam") steam++;
                else if (game.Platform == "GOG") gog++;
                else if (game.Platform == "Epic Games") epic++;
                else if (game.Platform == "Ubisoft Connect") ubi++;
                else if (game.Platform == "EA App") ea++;
                else if (game.Platform == "Xbox") xbox++;
            }

            StatusText = $"{_games.Count} Spiele | Steam: {steam} | GOG: {gog} | Epic: {epic} | Ubi: {ubi} | EA: {ea} | Xbox: {xbox} | Manuell: {manual}";
        }

        public void RefreshStatusText()
        {
            UpdateStatusText();
        }

        private void PopulateFilterOptions()
        {
            new Action(() =>
            {
                var currentFilter = _selectedFilter;
                
                var newOptions = new ObservableCollection<string>
                {
                    "Alle", "Steam", "Epic Games", "GOG", "Ubisoft Connect", "EA App", "Xbox", "Manuell", "Ausgeblendet"
                };

                var usedTags = _gameManager.GetAllUsedTags();
                if (usedTags.Count > 0 || Constants.Tags.DefaultTags.Length > 0)
                {
                    newOptions.Add("──────────");
                }

                foreach (var tag in Constants.Tags.DefaultTags)
                {
                    newOptions.Add($"🏷️ {tag}");
                }

                foreach (var tag in usedTags)
                {
                    if (!Constants.Tags.DefaultTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        newOptions.Add($"🏷️ {tag}");
                    }
                }
                
                FilterOptions = newOptions;

                // Restore selection and notify UI so the combobox doesn't appear empty
                if (!FilterOptions.Contains(currentFilter))
                {
                    currentFilter = "Alle";
                }
                
                _selectedFilter = currentFilter;
                OnPropertyChanged(nameof(SelectedFilter));
            }).RunOnUI();
        }
    }
}
