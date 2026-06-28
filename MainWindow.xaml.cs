using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using GameLauncher.Models;
using GameLauncher.Core;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    using GameLauncher.ViewModels;

    public partial class MainWindow : Window
    {
        private const double StartupPreloadVerticalBuffer = 1200;
        private const int StartupWarmupImageCount = 24;
        private static readonly TimeSpan StartupMetadataDelay = TimeSpan.FromMilliseconds(1200);
        private GameManager _gameManager = null!;
        private MainViewModel _viewModel = null!;
        private DataTemplate? _originalCardTemplate; // Store original XAML template
        
        private Services.PlayTimeService _playTimeService = null!;
        private Services.UISettingsService _uiSettingsService = null!;
        private Services.MainWindow.IGameCardLayoutService _gameCardLayoutService = null!;
        private Services.MainWindow.ITrayController _trayController = null!;
        private Services.MainWindow.IFpsCounter _fpsCounter = null!;
        private Services.MainWindow.IOverlayController _overlayController = null!;
        private Services.MainWindow.IStatusMessageService _statusMessageService = null!;
        private Services.MainWindow.IUpdateCoordinator _updateCoordinator = null!;
        private Services.MainWindow.AnimationService _animationService = null!;
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private UiSettingsSnapshot? _lastAppliedUiSettings;
        private Services.MainWindow.GameImageCacheController _imageCacheController = null!;
        private ViewMode _currentViewMode = ViewMode.Cards;
        private CardSize _currentCardSize = CardSize.Medium;
        private int _currentCardColumns = 1;
        private bool _resetViewportAfterNextRefresh;

        public static readonly DependencyProperty IsStartupActiveProperty =
            DependencyProperty.Register("IsStartupActive", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public bool IsStartupActive
        {
            get => (bool)GetValue(IsStartupActiveProperty);
            set => SetValue(IsStartupActiveProperty, value);
        }

        public static readonly DependencyProperty IsInitialLoadingProperty =
            DependencyProperty.Register("IsInitialLoading", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public bool IsInitialLoading
        {
            get => (bool)GetValue(IsInitialLoadingProperty);
            set => SetValue(IsInitialLoadingProperty, value);
        }

        public static readonly DependencyProperty AreAnimationsEnabledProperty =
            DependencyProperty.Register("AreAnimationsEnabled", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public bool AreAnimationsEnabled
        {
            get => (bool)GetValue(AreAnimationsEnabledProperty);
            set => SetValue(AreAnimationsEnabledProperty, value);
        }

        public static readonly DependencyProperty AreImageLoadTransitionsEnabledProperty =
            DependencyProperty.Register("AreImageLoadTransitionsEnabled", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public bool AreImageLoadTransitionsEnabled
        {
            get => (bool)GetValue(AreImageLoadTransitionsEnabledProperty);
            set => SetValue(AreImageLoadTransitionsEnabledProperty, value);
        }

        public MainWindow()
        {
            InitializeComponent();
            
            // Enable Dark Title Bar
            SourceInitialized += (s, e) => Services.DarkModeHelper.EnableDarkTitleBar(this);

            try 
            {
                _gameManager = new GameManager();
                _localization.ApplyLanguageCode(_gameManager.GetConfig().UISettings.LanguageCode);
                _viewModel = new MainViewModel(_gameManager);
                DataContext = _viewModel;

                _gameManager.GamesUpdated += OnGamesUpdatedInWindow;
                _viewModel.LibraryViewRefreshed += OnLibraryViewRefreshed;
            }
            catch (Exception ex)
            {
                Logger.Error("GameManager Init Failed", ex);
                MessageBox.Show(_localization.Format("App.GameManagerInitError", ex.Message), _localization.Get("Common.Error"));
            }
            
            // Zeilen-Template fuer den stabilen, virtualisierten Kartenmodus merken.
            _originalCardTemplate = GameListControl.ItemTemplate;

            ContentRendered += MainWindow_ContentRendered;

            _uiSettingsService = new Services.UISettingsService();
            _gameCardLayoutService = new Services.MainWindow.GameCardLayoutService(_uiSettingsService);
            _animationService = new Services.MainWindow.AnimationService();
            _imageCacheController = new Services.MainWindow.GameImageCacheController(GameListControl);
            _trayController = new Services.MainWindow.TrayController();
            _fpsCounter = new Services.MainWindow.FpsCounter();
            _overlayController = new Services.MainWindow.OverlayController();
            _statusMessageService = new Services.MainWindow.StatusMessageService(
                message =>
                {
                    if (_viewModel != null)
                    {
                        new Action(() => _viewModel.StatusText = message).RunOnUI();
                    }
                },
                () =>
                {
                    if (_viewModel != null)
                    {
                        new Action(() => _viewModel.RefreshStatusText()).RunOnUI();
                    }
                });
            _updateCoordinator = new Services.MainWindow.UpdateCoordinator("Airwolf995/GameLauncher");
            _localization.LanguageChanged += OnLanguageChanged;

            ApplySavedTheme();

            // FpsCounter bedarfsgesteuert: nur aktiv wenn Fenster sichtbar
            StateChanged += MainWindow_StateChanged;
        }

        private void InitOverlay()
        {
            _overlayController.Initialize(this, _playTimeService);
        }



        private void OnGamesUpdatedInWindow(object? sender, EventArgs e)
        {
            // Trigger visual refresh (animation/stats) when data updates
            new Action(() => RefreshList(instant: true)).RunOnUI();
        }

        private void InitPlayTimeService()
        {
            _playTimeService = new Services.PlayTimeService(_gameManager, _viewModel.Games);
            _playTimeService.PlayTimeUpdated += OnPlayTimeUpdated;
            _playTimeService.Start();
        }

        private void OnPlayTimeUpdated(object? sender, Game game)
        {
            // Game implements INotifyPropertyChanged — bindings update automatically.
            // No explicit Dispatcher.Invoke needed.
        }

        private void InitFpsCounter()
        {
            _fpsCounter.Start(fps => new Action(() => FpsText.Text = _localization.Format("Main.Fps", fps)).RunOnUI());
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                (_fpsCounter as Services.MainWindow.FpsCounter)?.Stop();
            }
            else
            {
                (_fpsCounter as Services.MainWindow.FpsCounter)?.Resume();
            }
        }

        private void InitTrayIcon()
        {
            _trayController.Initialize(RestoreFromTray, ExitApplication);
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayController.HideTrayIcon();
        }

        private bool _isExiting = false;

        internal static bool ShouldMinimizeToTrayOnClose(bool isExiting, bool minimizeToTray) =>
            !isExiting && minimizeToTray;

        private void BeginExit()
        {
            _isExiting = true;
        }

        private void ExitApplication()
        {
            BeginExit();
            _trayController?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Force immediate save of any pending changes before closing
            var gameManager = _gameManager;
            var config = gameManager?.GetConfig();
            if (gameManager != null && config != null)
            {
                gameManager.SaveConfigImmediate(config);
            }
             
            if (ShouldMinimizeToTrayOnClose(_isExiting, config?.UISettings.MinimizeToTray ?? false))
            {
                e.Cancel = true;
                Hide();
                _trayController?.ShowTrayIcon();
                _trayController?.ShowBalloon(_localization.Get("Main.TrayMinimizedTitle"), _localization.Get("Main.TrayMinimizedBody"), Constants.Timings.TrayBalloonDurationMs);
            }
            else
            {
                if (_gameManager != null) _gameManager.GamesUpdated -= OnGamesUpdatedInWindow;
                if (_playTimeService != null) _playTimeService.PlayTimeUpdated -= OnPlayTimeUpdated;

                _playTimeService?.Dispose();
                _overlayController?.Dispose();
                _trayController?.Dispose();
                _fpsCounter?.Dispose();
                _statusMessageService?.Dispose();
                if (_viewModel != null) _viewModel.LibraryViewRefreshed -= OnLibraryViewRefreshed;
                _viewModel?.Dispose();
                _gameManager?.Dispose();
                _localization.LanguageChanged -= OnLanguageChanged;
                _imageCacheController?.Dispose();
                base.OnClosing(e);
            }
        }


        private async void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;

            try
            {
                InitTrayIcon();
                InitFpsCounter();

                // Check if it's the first start to show the Wizard
                if (_gameManager.GetConfig().UISettings.FirstStart)
                {
                    IsInitialLoading = false;
                    var wizard = new SetupWizardWindow(_gameManager)
                    {
                        Owner = this
                    };
                    wizard.ShowDialog();
                }

                // Apply UI Settings
                var startupUiSettings = _gameManager.GetConfig().UISettings;
                ApplyUISettings(startupUiSettings, registerHotkey: false, writeLog: true);

                // Load Games via ViewModel
                IsInitialLoading = true;
                await _viewModel.LoadGamesAsync(
                    loadSteamMetadataInBackground: false,
                    includeDeferredStartupGames: true);
                InitPlayTimeService();
                InitOverlay();
                ApplyUISettings(startupUiSettings, registerHotkey: true, writeLog: false);
                await Dispatcher.InvokeAsync(() => GameListControl.UpdateLayout(), DispatcherPriority.Loaded);
                // Während das Lade-Overlay sichtbar ist, die Bibliothek sofort final aufbauen.
                RefreshList(instant: true);
                await Dispatcher.InvokeAsync(() => GameListControl.UpdateLayout(), DispatcherPriority.Loaded);
                try
                {
                    var startupPreloadPaths = CollectStartupPreloadPaths(StartupPreloadVerticalBuffer, StartupWarmupImageCount);
                    if (startupPreloadPaths.Count > 0)
                    {
                        Logger.Log($"Startup-Bildvorwärmung gestartet: {startupPreloadPaths.Count} Cover.");
                        await BitmapCacheConverter.PreloadAsync(startupPreloadPaths);
                    }

                    var visibleStartupPaths = _imageCacheController.GetBufferedImagePaths(0);
                    if (visibleStartupPaths.Count > 0)
                    {
                        Logger.Log($"Startup-Sichtbereich wird gezielt vorgewärmt: {visibleStartupPaths.Count} Cover.");
                        await BitmapCacheConverter.PreloadAsync(visibleStartupPaths);
                    }

                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await _imageCacheController.WaitForVisibleImagesReadyAsync(TimeSpan.FromSeconds(4));
                    await _imageCacheController.RefreshVisibleImagesAsync();
                    await Dispatcher.InvokeAsync(() => GameListControl.UpdateLayout(), DispatcherPriority.Loaded);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    BitmapCacheConverter.ReleaseStartupStrongCache();
                }
                catch (Exception ex)
                {
                    BitmapCacheConverter.ReleaseStartupStrongCache();
                    Logger.Error("Warten auf sichtbare Startbilder fehlgeschlagen.", ex);
                }
                AreImageLoadTransitionsEnabled = false;
                IsInitialLoading = false;
                
                // Check for updates (if enabled in settings)
                var settings = _gameManager?.GetConfig()?.UISettings;
                if (settings?.AutoCheckUpdates ?? true)
                {
                    _ = CheckForUpdatesAsync();
                }
                
                
                Logger.Log("MainWindow loaded and ready.");
                _ = StartDeferredMetadataRefreshAsync();
            }

            catch (Exception ex)
            {
                 IsInitialLoading = false;
                 Logger.Error("Error loading games in MainWindow", ex);
                 MessageBox.Show(_localization.Format("App.LoadError", ex.Message), _localization.Get("Common.Error"));
            }

        }

        private async Task StartDeferredMetadataRefreshAsync()
        {
            try
            {
                await Task.Delay(StartupMetadataDelay);
                await _viewModel.RefreshSteamMetadataAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Zeitversetzte Steam-Metadatenaktualisierung fehlgeschlagen.", ex);
            }
        }

        private async Task StartDeferredStartupGameLoadAsync()
        {
            try
            {
                var deferredGames = await _gameManager.LoadDeferredStartupGamesAsync();
                if (deferredGames.Count == 0)
                {
                    return;
                }

                await _viewModel.MergeGamesAsync(deferredGames);
                Logger.Log($"Zeitversetzte Startup-Spiele übernommen: {deferredGames.Count} Einträge.");
                RefreshList(instant: true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Zeitversetztes Startup-Nachladen fehlgeschlagen.", ex);
            }
        }

        private void ApplySavedTheme()
        {
            string savedTheme = _gameManager.GetConfig().Theme;
            if (string.IsNullOrEmpty(savedTheme))
            {
                return;
            }

            string colorCode = Constants.UI.GetColorCodeForTheme(savedTheme);
            _uiSettingsService.ApplyTheme(colorCode);
        }





        private bool NavigateList(int direction)
        {
            if (_currentViewMode == ViewMode.Cards)
            {
                return false;
            }

            if (GameListControl.Items.Count == 0) return false;

            int currentIndex = GameListControl.SelectedIndex;
            if (currentIndex == -1) currentIndex = 0;

            int newIndex = currentIndex;
            int columns = (int)(GameListControl.ActualWidth / 420); 
            if (columns < 1) columns = 1;

            switch (direction)
            {
                case 1: newIndex++; break; // Right
                case -1: newIndex--; break; // Left
                case 2: newIndex += columns; break; // Down
                case -2: newIndex -= columns; break; // Up
            }

            // Boundary Check for UP (Escaping to Header)
            if (newIndex < 0 && direction == -2) return false;

            // Clamp
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= GameListControl.Items.Count) newIndex = GameListControl.Items.Count - 1;

            if (newIndex != currentIndex)
            {
                GameListControl.SelectedIndex = newIndex;
                GameListControl.ScrollIntoView(GameListControl.Items[newIndex]);
                var container = GameListControl.ItemContainerGenerator.ContainerFromIndex(newIndex) as ListBoxItem;
                container?.Focus();
            }
            return true;
        }

        private void ComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not ComboBox comboBox)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() => AlignComboBoxDropDown(comboBox)),
                DispatcherPriority.Loaded);
        }

        private static void AlignComboBoxDropDown(ComboBox comboBox)
        {
            if (comboBox.SelectedIndex < 0)
            {
                return;
            }

            if (comboBox.Template.FindName("Popup", comboBox) is not Popup popup || popup.Child is not DependencyObject popupChild)
            {
                return;
            }

            var scrollViewer = popupChild.FindDescendant<ScrollViewer>();
            if (scrollViewer == null)
            {
                return;
            }

            comboBox.UpdateLayout();

            int anchorIndex = Math.Max(0, comboBox.SelectedIndex - 1);
            if (comboBox.ItemContainerGenerator.ContainerFromIndex(anchorIndex) is not ComboBoxItem anchorItem)
            {
                return;
            }

            var top = anchorItem.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
            if (Math.Abs(top) > 0.5)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + top));
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T typedCurrent) return typedCurrent;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        private void RefreshList(bool instant = true)
        {
            // Re-animate items when list is refreshed/filtered
            Dispatcher.BeginInvoke(new Action(() => {
                AnimateItemsStaggered(instant);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private IReadOnlyList<string> CollectStartupPreloadPaths(double verticalBuffer, int maxImageCount)
        {
            var imagePaths = new List<string>(maxImageCount);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in _imageCacheController.GetBufferedImagePaths(verticalBuffer))
            {
                if (imagePaths.Count >= maxImageCount)
                {
                    break;
                }

                if (seenPaths.Add(path))
                {
                    imagePaths.Add(path);
                }
            }

            foreach (var item in GameListControl.Items)
            {
                if (imagePaths.Count >= maxImageCount)
                {
                    break;
                }

                switch (item)
                {
                    case Game game when !string.IsNullOrWhiteSpace(game.ImageUrl):
                        if (seenPaths.Add(game.ImageUrl))
                        {
                            imagePaths.Add(game.ImageUrl);
                        }
                        break;
                    case GameRow row:
                        foreach (var rowGame in row.Games)
                        {
                            if (imagePaths.Count >= maxImageCount)
                            {
                                break;
                            }

                            if (!string.IsNullOrWhiteSpace(rowGame.ImageUrl) && seenPaths.Add(rowGame.ImageUrl))
                            {
                                imagePaths.Add(rowGame.ImageUrl);
                            }
                        }
                        break;
                }
            }

            return imagePaths;
        }
        
        // Fix for ClearSearch_Click build error
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SearchText = string.Empty;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = _localization.Get("AppName");
            FpsText.Text = _localization.Format("Main.Fps", 0);
            _imageCacheController.ScheduleViewportRetentionUpdate();
        }

        private void LibraryViewStateChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || IsInitialLoading)
            {
                return;
            }

            string sourceName = sender switch
            {
                FrameworkElement element when !string.IsNullOrWhiteSpace(element.Name) => element.Name,
                _ => sender.GetType().Name
            };
            Logger.Log($"Bibliotheksansicht geändert: Auslöser={sourceName}, Einträge={GameListControl.Items.Count}.");

            if (ReferenceEquals(sender, FilterBox) || ReferenceEquals(sender, SortBox))
            {
                _resetViewportAfterNextRefresh = true;
            }
        }

        private void OnLibraryViewRefreshed(object? sender, MainViewModel.LibraryViewRefreshedEventArgs e)
        {
            if (!IsLoaded || IsInitialLoading)
            {
                return;
            }

            if (_resetViewportAfterNextRefresh)
            {
                ResetLibraryViewportToTop();
                _resetViewportAfterNextRefresh = false;
            }

            _imageCacheController.StartPriorityWarmup(e.InitialWarmupImagePaths);
            _imageCacheController.ScheduleViewportRetentionUpdate();
            _imageCacheController.ScheduleViewChangeStabilization();

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (GameListControl.FindDescendant<GameLauncher.Controls.VirtualizingWrapPanel>() is GameLauncher.Controls.VirtualizingWrapPanel wrapPanel)
                    {
                        var realizedRange = wrapPanel.GetRealizedIndexRange();
                        int realizedCount = Math.Max(0, realizedRange.lastIndexExclusive - realizedRange.firstIndex);
                        Logger.Log($"Virtualisierung nach Ansichtswechsel: realisiert={realizedCount}, Bereich={realizedRange.firstIndex}-{Math.Max(realizedRange.firstIndex, realizedRange.lastIndexExclusive - 1)}, Gesamt={GameListControl.Items.Count}.");
                    }
                    else
                    {
                        Logger.Log($"Virtualisierung nach Ansichtswechsel: Zeilenmodus aktiv, realisierte Zeilen derzeit nicht separat erfasst, Gesamt={GameListControl.Items.Count}.");
                    }
                }),
                DispatcherPriority.Loaded);
        }

        private void ResetLibraryViewportToTop()
        {
            if (GameListControl.FindDescendant<ScrollViewer>() is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(0);
            }
        }


        #region Event Handlers
        // Event handlers for Search/Filter/Sort Removed - Handled by ViewModel

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow(_gameManager, _uiSettingsService.ApplyTheme, ApplyUISettingsPreview);
            settings.Owner = this;
            if (settings.ShowDialog() == true)
            {
                // Apply UI settings immediately without restart
                ApplyUISettings();
            }
        }

        private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (IsInitialLoading)
            {
                return;
            }

            bool previousImageLoadTransitionsEnabled = AreImageLoadTransitionsEnabled;
            bool refreshCompleted = false;

            try
            {
                AreImageLoadTransitionsEnabled = true;
                IsInitialLoading = true;
                await _viewModel.LoadGamesAsync(includeDeferredStartupGames: true);
                RefreshList(instant: false);
                refreshCompleted = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Manual library refresh failed", ex);
                MessageBox.Show(_localization.Format("App.LoadError", ex.Message), _localization.Get("Common.Error"));
            }
            finally
            {
                if (!refreshCompleted)
                {
                    AreImageLoadTransitionsEnabled = previousImageLoadTransitionsEnabled;
                }

                IsInitialLoading = false;
            }
        }



        private void ApplyUISettings()
        {
            ApplyUISettings(_gameManager.GetConfig().UISettings, registerHotkey: true, writeLog: true);
        }

        private void ApplyUISettingsPreview(UISettings uiSettings)
        {
            ApplyUISettings(uiSettings, registerHotkey: false, writeLog: false);
        }

        private void ApplyUISettings(UISettings uiSettings, bool registerHotkey, bool writeLog)
        {
            var snapshot = UiSettingsSnapshot.From(uiSettings);
            if (_lastAppliedUiSettings == snapshot)
            {
                return;
            }

            var previous = _lastAppliedUiSettings;
            
            if (previous == null || previous.CardSize != snapshot.CardSize)
            {
                ApplyCardSize(uiSettings.CardSize, false);
            }
            
            if (previous == null ||
                previous.ViewMode != snapshot.ViewMode ||
                previous.CardSize != snapshot.CardSize)
            {
                ApplyViewMode(uiSettings.ViewMode, uiSettings.CardSize, false);
            }
            
            if (previous == null || previous.AnimationsEnabled != snapshot.AnimationsEnabled)
            {
                ApplyAnimations(uiSettings.AnimationsEnabled, writeLog);
            }
            
            if (previous == null || Math.Abs(previous.FontScale - snapshot.FontScale) > 0.0001)
            {
                _uiSettingsService.ApplyFontScale(uiSettings.FontScale, this.Content as Grid, writeLog);
            }
            
            if (previous == null || !string.Equals(previous.BackgroundImage, snapshot.BackgroundImage, StringComparison.Ordinal))
            {
                _uiSettingsService.ApplyBackgroundImage(uiSettings.BackgroundImage, BackgroundImage);
            }
            
            if (writeLog)
            {
                Logger.Log($"UI Settings applied: CardSize={uiSettings.CardSize}, ViewMode={uiSettings.ViewMode}, Animations={uiSettings.AnimationsEnabled}, FontScale={uiSettings.FontScale}");
            }

            if (registerHotkey && IsLoaded)
            {
                _overlayController.RegisterHotkey(this, uiSettings);
            }

            _lastAppliedUiSettings = snapshot;
        }

        private void ApplyCardSize(Models.CardSize size, bool refresh = true)
        {
            _currentCardSize = size;
            _gameCardLayoutService.ApplyCardSize(GameListControl, Resources, size, refresh);
            UpdateCardRowsLayout();
        }

        private void ApplyViewMode(Models.ViewMode mode, Models.CardSize size, bool refresh = true)
        {
            _currentViewMode = mode;
            _currentCardSize = size;
            BindingOperations.SetBinding(
                GameListControl,
                ItemsControl.ItemsSourceProperty,
                new Binding(mode == ViewMode.Cards ? nameof(MainViewModel.CardRows) : nameof(MainViewModel.GamesView)));
            GameListControl.ItemContainerStyle = mode == ViewMode.Cards
                ? Resources["GameRowItemContainerStyle"] as Style
                : Resources["GameListItemContainerStyle"] as Style;

            var action = _gameCardLayoutService.ApplyViewMode(
                GameListControl,
                Resources,
                mode,
                _originalCardTemplate,
                size,
                refresh);

            UpdateCardRowsLayout();

            if (action == Services.MainWindow.ViewModeAnimationAction.Animate)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AnimateItemsStaggered();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else if (action == Services.MainWindow.ViewModeAnimationAction.AnimateInstant)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AnimateItemsStaggered(true);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void GameListControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || _currentViewMode != ViewMode.Cards)
            {
                return;
            }

            UpdateCardRowsLayout();
        }

        private void UpdateCardRowsLayout()
        {
            if (_viewModel == null || _currentViewMode != ViewMode.Cards || GameListControl.ActualWidth <= 0)
            {
                return;
            }

            var layoutResult = _gameCardLayoutService.ApplyCardRowLayout(
                Resources,
                GameListControl.ActualWidth,
                _currentCardSize,
                _currentCardColumns);

            _currentCardColumns = layoutResult.Columns;
            if (_viewModel.UpdateCardColumns(layoutResult.Columns))
            {
                Logger.Log($"Kartenzeilen aktualisiert: Breite={GameListControl.ActualWidth:0.#}, Spalten={layoutResult.Columns}, Kartenbreite={layoutResult.CardWidth:0.#}, Kartengröße={_currentCardSize}.");
            }
        }

        private void ApplyAnimations(bool enabled, bool writeLog = true)
        {
            // Toggle the dependency property so XAML triggers can react
            this.AreAnimationsEnabled = enabled;
            if (writeLog)
            {
                Logger.Log($"Animations {(enabled ? "enabled" : "disabled")}");
            }
        }

        private sealed record UiSettingsSnapshot(
            Models.CardSize CardSize,
            Models.ViewMode ViewMode,
            bool AnimationsEnabled,
            double FontScale,
            string BackgroundImage,
            bool OverlayHotkeyCtrl,
            bool OverlayHotkeyAlt,
            bool OverlayHotkeyShift,
            bool OverlayHotkeyWin,
            string OverlayHotkeyKey)
        {
            public static UiSettingsSnapshot From(UISettings settings) =>
                new(
                    settings.CardSize,
                    settings.ViewMode,
                    settings.AnimationsEnabled,
                    settings.FontScale,
                    settings.BackgroundImage ?? "",
                    settings.OverlayHotkeyCtrl,
                    settings.OverlayHotkeyAlt,
                    settings.OverlayHotkeyShift,
                    settings.OverlayHotkeyWin,
                    settings.OverlayHotkeyKey ?? "");
        }

        private async void AddGame_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = _gameManager.GetConfig().UISettings.SteamGridDbApiKey;
            var dialog = new AddGameWindow(apiKey) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // Add game in manager but don't trigger the global event (that would cause a full reload/re-animation)
                var newGame = _gameManager.AddManualGame(dialog.GameName, dialog.GamePath, dialog.GameArgs, dialog.GameCoverPath, notifyUI: false);
                
                // Add to our main collection instantly via ViewModel
                _viewModel.Games.Add(newGame);
                
                await _viewModel.RebuildLibraryViewAsync();
                ShowStatus(_localization.Get("Main.StatusGameAdded"));
                Logger.Log("User added a manual game. Added instantly to list.");
                
                // Refresh instantly so the new item (Opacity 0) becomes visible immediately
                RefreshList(instant: true);
            }
        }

        private void GameCard_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGameFromSender(sender, out Game? game))
            {
                OpenGameDetails(game!);
            }
        }

        private void GameTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (!TryGetGameFromSender(sender, out Game? game))
            {
                return;
            }

            OpenGameDetails(game!);
            e.Handled = true;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
               LaunchGame(game);
            }
        }

        private async void ChangeImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = _localization.Get("Main.ChangeImageDialogFilter"),
                    Title = _localization.Get("Main.ChangeImageDialogTitle")
                };

                if (dialog.ShowDialog() == true)
                {
                    _gameManager.SetManualGameImage(game, dialog.FileName, notifyUI: false);
                    await _viewModel.RebuildLibraryViewAsync();
                    RefreshList(instant: true);
                    ShowStatus(_localization.Get("Main.StatusImageUpdated"));
                }
            }
        }

        private async void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                _gameManager.ToggleFavorite(game, notifyUI: false);
                 await _viewModel.RebuildLibraryViewAsync();
                 RefreshList(instant: true);
                 ShowStatus(game.IsFavorite ? _localization.Get("Main.StatusFavoriteAdded") : _localization.Get("Main.StatusFavoriteRemoved"));
            }
        }

        private async void Hide_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                 if (game.IsHidden)
                 {
                     _gameManager.UnhideGame(game, notifyUI: false);
                     ShowStatus(_localization.Get("Main.StatusGameShown"));
                 }
                 else
                 {
                     if (MessageBox.Show(_localization.Format("Main.HideConfirmBody", game.Name), _localization.Get("Main.HideConfirmTitle"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                     {
                         _gameManager.HideGame(game, notifyUI: false);
                         ShowStatus(_localization.Get("Main.StatusGameHidden"));
                     }
                 }
                 await _viewModel.RebuildLibraryViewAsync();
                 RefreshList(instant: true);
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                if (ModernMessageWindow.Show(
                    _localization.Format("Main.DeleteConfirmBody", game.Name),
                    _localization.Get("Main.DeleteConfirmTitle"),
                    ModernMessageWindow.ModernMessageButton.YesNo,
                    this) == MessageBoxResult.Yes)
                {
                    // Remove but don't trigger global update
                    _gameManager.RemoveManualGame(game, notifyUI: false);
                    
                    // Remove from our visible collection instantly via ViewModel
                    _viewModel.Games.Remove(game);
                    
                    await _viewModel.RebuildLibraryViewAsync();
                    ShowStatus(_localization.Get("Main.StatusGameDeleted"));
                    RefreshList(instant: true);
                }
            }
        }

        private void LaunchGame(Game game)
        {
            try
            {
                _gameManager.LaunchGame(game, notifyUI: false);
                
                // Update LastPlayed immediately on launch
                game.LastPlayed = DateTime.Now;
                _gameManager.UpdateLastPlayed(game.Id, game.LastPlayed.Value);
                _gameManager.NotifyGamesUpdated();

                ShowStatus(_localization.Format("Main.StatusLaunching", game.Name));

                // Handle launcher behavior on game start
                var settings = _gameManager?.GetConfig()?.UISettings;
                if (settings != null)
                {
                    if (settings.CloseOnGameStart)
                    {
                        // Mark as explicit exit so OnClosing bypasses MinimizeToTray.
                        BeginExit();
                        Close();
                    }
                    else if (settings.MinimizeOnGameStart)
                    {
                        this.WindowState = WindowState.Minimized;
                    }
                }
            }
            catch (Exception ex)
            {
                // Logger.Error handled in GameManager
                if (ex.Message.Contains("find") || ex is System.ComponentModel.Win32Exception)
                {
                     MessageBox.Show(_localization.Format("Main.FileMissingBody", game.Path), _localization.Get("Main.FileMissingTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                     MessageBox.Show(_localization.Get("Main.LaunchErrorBody"), _localization.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
                ShowStatus(_localization.Get("Main.StatusError"));
            }
        }

        private void OpenGameDetails(Game game)
        {
            var details = new GameDetailsWindow(game, _gameManager);
            details.Owner = this;
            details.LaunchGameRequested += LaunchGame;
            Logger.Log($"Opening details for: {game.Name}");
            details.ShowDialog();

            if (details.GameWasModified)
            {
                RefreshList(instant: true);
            }
        }

        private static bool TryGetGameFromSender(object sender, out Game? game)
        {
            game = sender switch
            {
                FrameworkElement element when element.DataContext is Game senderGame => senderGame,
                _ => null
            };

            return game != null;
        }





        private async void AnimateItemsStaggered(bool instant = false)
        {
            await _animationService.AnimateItemsStaggeredAsync(
                GameListControl,
                active => IsStartupActive = active,
                instant);

            if (!instant)
            {
                AreImageLoadTransitionsEnabled = false;
            }

            _imageCacheController.ScheduleViewportRetentionUpdate();
        }

        private void ShowStatus(string message, int delayMs = 3000) =>
            _statusMessageService.ShowStatus(message, delayMs);

        private Task CheckForUpdatesAsync() =>
            _updateCoordinator.CheckForUpdatesAsync(this);


        #endregion
    }
}
