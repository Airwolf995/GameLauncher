using System;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using GameLauncher.Controls;
using GameLauncher.Models;
using GameLauncher.Core;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    using GameLauncher.ViewModels;

    public partial class MainWindow : Window
    {
        private const double StartupPreloadVerticalBuffer = 1200;
        private const double ViewportRetentionVerticalBuffer = 300;
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
        private bool _releaseStartupImageCacheAfterAnimation;
        private bool _startupImageCacheTrackingActive;
        private bool _startupAnimationCompleted;
        private int _startupImageBindingsRemaining;
        private CancellationTokenSource? _startupImageCacheReleaseCts;
        private readonly HashSet<Image> _startupBoundImages = new();
        private readonly DispatcherTimer _visibleImageRetentionTimer;

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
            }
            catch (Exception ex)
            {
                Logger.Error("GameManager Init Failed", ex);
                MessageBox.Show(_localization.Format("App.GameManagerInitError", ex.Message), _localization.Get("Common.Error"));
            }
            
            // Save original card template and style for restoration
            _originalCardTemplate = GameListControl.ItemTemplate;

            ContentRendered += MainWindow_ContentRendered;

            _uiSettingsService = new Services.UISettingsService();
            _gameCardLayoutService = new Services.MainWindow.GameCardLayoutService(_uiSettingsService);
            _animationService = new Services.MainWindow.AnimationService();
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
            _visibleImageRetentionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _visibleImageRetentionTimer.Tick += VisibleImageRetentionTimer_Tick;

            ApplySavedTheme();

            // FpsCounter bedarfsgesteuert: nur aktiv wenn Fenster sichtbar
            StateChanged += MainWindow_StateChanged;
            GameListControl.LayoutUpdated += GameListControl_LayoutUpdated;
            GameListControl.SizeChanged += GameListControl_SizeChanged;
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
                _viewModel?.Dispose();
                _gameManager?.Dispose();
                _localization.LanguageChanged -= OnLanguageChanged;
                _visibleImageRetentionTimer.Stop();
                GameListControl.LayoutUpdated -= GameListControl_LayoutUpdated;
                GameListControl.SizeChanged -= GameListControl_SizeChanged;
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
                InitPlayTimeService();
                InitOverlay();

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
                ApplyUISettings();

                // Load Games via ViewModel
                IsInitialLoading = true;
                await _viewModel.LoadGamesAsync();
                await Dispatcher.InvokeAsync(() => GameListControl.UpdateLayout(), DispatcherPriority.Loaded);
                try
                {
                    await BitmapCacheConverter.PreloadAsync(
                        GetBufferedImagePathsForCurrentViewport(verticalBuffer: StartupPreloadVerticalBuffer));
                    _releaseStartupImageCacheAfterAnimation = true;
                }
                catch (Exception ex)
                {
                    _releaseStartupImageCacheAfterAnimation = false;
                    ResetStartupImageCacheTracking();
                    Logger.Error("Image preload failed. Continuing startup without preloaded covers.", ex);
                }
                IsInitialLoading = false;
                
                // Show the initial library with the configured startup animation.
                RefreshList(instant: false);
                
                // Check for updates (if enabled in settings)
                var settings = _gameManager?.GetConfig()?.UISettings;
                if (settings?.AutoCheckUpdates ?? true)
                {
                    _ = CheckForUpdatesAsync();
                }
                
                
                Logger.Log("MainWindow loaded and ready.");
            }

            catch (Exception ex)
            {
                 IsInitialLoading = false;
                 Logger.Error("Error loading games in MainWindow", ex);
                 MessageBox.Show(_localization.Format("App.LoadError", ex.Message), _localization.Get("Common.Error"));
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

            var scrollViewer = FindVisualChild<ScrollViewer>(popupChild);
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
        
        // Fix for ClearSearch_Click build error
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SearchText = string.Empty;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = _localization.Get("AppName");
            FpsText.Text = _localization.Format("Main.Fps", 0);
            ScheduleVisibleImageRetentionUpdate();
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
                await _viewModel.LoadGamesAsync();
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
            _gameCardLayoutService.ApplyCardSize(GameListControl, Resources, size, refresh);
        }

        private void ApplyViewMode(Models.ViewMode mode, Models.CardSize size, bool refresh = true)
        {
            var action = _gameCardLayoutService.ApplyViewMode(
                GameListControl,
                Resources,
                mode,
                _originalCardTemplate,
                size,
                refresh);

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

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void GameImage_TargetUpdated(object sender, DataTransferEventArgs e)
        {
            if (sender is not Image image)
            {
                return;
            }

            if (image.Source == null)
            {
                image.Opacity = AreImageLoadTransitionsEnabled ? 0 : 1;
                return;
            }

            if (!AreImageLoadTransitionsEnabled)
            {
                image.BeginAnimation(UIElement.OpacityProperty, null);
                image.Opacity = 1;
                return;
            }

            TrackStartupImageBinding(image);

            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Opacity = 0;
            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }

        private void AddGame_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = _gameManager.GetConfig().UISettings.SteamGridDbApiKey;
            var dialog = new AddGameWindow(apiKey) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // Add game in manager but don't trigger the global event (that would cause a full reload/re-animation)
                var newGame = _gameManager.AddManualGame(dialog.GameName, dialog.GamePath, dialog.GameArgs, dialog.GameCoverPath, notifyUI: false);
                
                // Add to our main collection instantly via ViewModel
                _viewModel.Games.Add(newGame);
                
                ShowStatus(_localization.Get("Main.StatusGameAdded"));
                Logger.Log("User added a manual game. Added instantly to list.");
                
                // Refresh instantly so the new item (Opacity 0) becomes visible immediately
                RefreshList(instant: true);
            }
        }

        private void GameCard_Click(object sender, RoutedEventArgs e)
        {
            // Handle ListBoxItem click (sender is ListBoxItem, DataContext is Game)
            if (sender is ListBoxItem item && item.DataContext is Game game)
            {
                var details = new GameDetailsWindow(game, _gameManager);
                details.Owner = this;
                details.LaunchGameRequested += (g) => LaunchGame(g);
                Logger.Log($"Opening details for: {game.Name}");
                details.ShowDialog();
                
                // Refresh list only if something changed (Played or Favorite toggled)
                if (details.GameWasModified)
                {
                    RefreshList(instant: true);
                }
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
               LaunchGame(game);
            }
        }

        private void ChangeImage_Click(object sender, RoutedEventArgs e)
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
                    _gameManager.SetManualGameImage(game, dialog.FileName);
                    _viewModel.GamesView.Refresh();
                    ShowStatus(_localization.Get("Main.StatusImageUpdated"));
                }
            }
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                _gameManager.ToggleFavorite(game);
                 _viewModel.GamesView.Refresh();
                 ShowStatus(game.IsFavorite ? _localization.Get("Main.StatusFavoriteAdded") : _localization.Get("Main.StatusFavoriteRemoved"));
            }
        }

        private void Hide_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                 if (game.IsHidden)
                 {
                     _gameManager.UnhideGame(game);
                     ShowStatus(_localization.Get("Main.StatusGameShown"));
                 }
                 else
                 {
                     if (MessageBox.Show(_localization.Format("Main.HideConfirmBody", game.Name), _localization.Get("Main.HideConfirmTitle"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                     {
                         _gameManager.HideGame(game);
                         ShowStatus(_localization.Get("Main.StatusGameHidden"));
                     }
                 }
                 RefreshList();
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
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
                    
                    ShowStatus(_localization.Get("Main.StatusGameDeleted"));
                    RefreshList(instant: true);
                }
            }
        }

        private void LaunchGame(Game game)
        {
            try
            {
                _gameManager.LaunchGame(game);
                
                // Update LastPlayed immediately on launch
                game.LastPlayed = DateTime.Now;
                _gameManager.UpdateLastPlayed(game.Id, game.LastPlayed.Value);

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





        private async void AnimateItemsStaggered(bool instant = false)
        {
            if (!instant && _releaseStartupImageCacheAfterAnimation)
            {
                await Dispatcher.InvokeAsync(
                    PrepareStartupImageCacheRelease,
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            await _animationService.AnimateItemsStaggeredAsync(
                GameListControl,
                active => IsStartupActive = active,
                instant);

            if (!instant)
            {
                AreImageLoadTransitionsEnabled = false;
            }

            ScheduleVisibleImageRetentionUpdate();

            if (!instant && _releaseStartupImageCacheAfterAnimation)
            {
                _startupAnimationCompleted = true;
                TryReleaseStartupImageCache();
            }
        }

        private void PrepareStartupImageCacheRelease()
        {
            _startupImageCacheReleaseCts?.Cancel();
            _startupImageCacheReleaseCts?.Dispose();

            var startupImages = GetGeneratedStartupImages();
            int expectedImageCount = startupImages.Count;

            _startupAnimationCompleted = false;
            _startupImageCacheTrackingActive = expectedImageCount > 0;
            _startupImageBindingsRemaining = expectedImageCount;
            _startupBoundImages.Clear();

            if (!_startupImageCacheTrackingActive)
            {
                return;
            }

            Logger.Log($"Startup image cache tracking started: waiting for {expectedImageCount} image binding(s).");

            foreach (var image in startupImages)
            {
                if (image.Source != null && _startupBoundImages.Add(image))
                {
                    _startupImageBindingsRemaining = Math.Max(0, _startupImageBindingsRemaining - 1);
                }
            }

            _startupImageCacheReleaseCts = new CancellationTokenSource();
            _ = ForceReleaseStartupImageCacheAfterTimeoutAsync(_startupImageCacheReleaseCts.Token);
        }

        private void TrackStartupImageBinding(Image image)
        {
            if (!_startupImageCacheTrackingActive)
            {
                return;
            }

            if (!_startupBoundImages.Add(image))
            {
                return;
            }

            _startupImageBindingsRemaining = Math.Max(0, _startupImageBindingsRemaining - 1);
            TryReleaseStartupImageCache();
        }

        private void TryReleaseStartupImageCache()
        {
            if (!_releaseStartupImageCacheAfterAnimation || !_startupAnimationCompleted)
            {
                return;
            }

            if (_startupImageCacheTrackingActive && _startupImageBindingsRemaining > 0)
            {
                return;
            }

            BitmapCacheConverter.ReleaseStartupStrongCache();
            _releaseStartupImageCacheAfterAnimation = false;
            ResetStartupImageCacheTracking();
        }

        private async Task ForceReleaseStartupImageCacheAfterTimeoutAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(1500, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!_releaseStartupImageCacheAfterAnimation)
                {
                    return;
                }

                Logger.Log(
                    $"Startup image cache release timeout reached. Remaining image bindings: {_startupImageBindingsRemaining}.");
                _startupImageBindingsRemaining = 0;
                TryReleaseStartupImageCache();
            });
        }

        private void ResetStartupImageCacheTracking()
        {
            _startupImageCacheTrackingActive = false;
            _startupAnimationCompleted = false;
            _startupImageBindingsRemaining = 0;
            _startupBoundImages.Clear();
            _startupImageCacheReleaseCts?.Cancel();
            _startupImageCacheReleaseCts?.Dispose();
            _startupImageCacheReleaseCts = null;
        }

        private List<Image> GetGeneratedStartupImages()
        {
            var images = new List<Image>();

            for (int i = 0; i < GameListControl.Items.Count; i++)
            {
                if (GameListControl.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                {
                    continue;
                }

                var image = FindVisualChild<Image>(container);
                if (image != null)
                {
                    images.Add(image);
                }
            }

            return images;
        }

        private void ShowStatus(string message, int delayMs = 3000) =>
            _statusMessageService.ShowStatus(message, delayMs);

        private Task CheckForUpdatesAsync() =>
            _updateCoordinator.CheckForUpdatesAsync(this);

        private void GameListControl_LayoutUpdated(object? sender, EventArgs e) =>
            ScheduleVisibleImageRetentionUpdate();

        private void GameListControl_SizeChanged(object sender, SizeChangedEventArgs e) =>
            ScheduleVisibleImageRetentionUpdate();

        private void ScheduleVisibleImageRetentionUpdate()
        {
            _visibleImageRetentionTimer.Stop();
            _visibleImageRetentionTimer.Start();
        }

        private void VisibleImageRetentionTimer_Tick(object? sender, EventArgs e)
        {
            _visibleImageRetentionTimer.Stop();
            UpdateVisibleImageRetention();
        }

        private void UpdateVisibleImageRetention()
        {
            if (FindVisualChild<ScrollViewer>(GameListControl) is not ScrollViewer scrollViewer)
            {
                return;
            }

            BitmapCacheConverter.UpdateViewportRetention(
                GetBufferedImagePathsForCurrentViewport(scrollViewer, ViewportRetentionVerticalBuffer));
        }

        private IReadOnlyList<string> GetBufferedImagePathsForCurrentViewport(
            ScrollViewer? scrollViewer = null,
            double verticalBuffer = ViewportRetentionVerticalBuffer)
        {
            scrollViewer ??= FindVisualChild<ScrollViewer>(GameListControl);
            if (scrollViewer == null)
            {
                return [];
            }

            if (FindVisualChild<VirtualizingWrapPanel>(GameListControl) is VirtualizingWrapPanel wrapPanel)
            {
                var range = wrapPanel.GetBufferedIndexRange(verticalBuffer);
                return CollectImagePathsFromItemRange(range.firstIndex, range.lastIndexExclusive);
            }

            var visiblePaths = new List<string>();
            Rect viewportRect = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
            viewportRect.Inflate(0, verticalBuffer);

            for (int i = 0; i < GameListControl.Items.Count; i++)
            {
                if (GameListControl.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                {
                    continue;
                }

                Rect containerBounds = container.TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (containerBounds.IntersectsWith(viewportRect) &&
                    container.DataContext is Game game &&
                    !string.IsNullOrWhiteSpace(game.ImageUrl))
                {
                    visiblePaths.Add(game.ImageUrl);
                }
            }

            return visiblePaths;
        }

        private List<string> CollectImagePathsFromItemRange(int firstIndex, int lastIndexExclusive)
        {
            if (firstIndex >= lastIndexExclusive)
            {
                return [];
            }

            var retainedPaths = new List<string>(Math.Max(0, lastIndexExclusive - firstIndex));
            for (int i = firstIndex; i < lastIndexExclusive; i++)
            {
                if (GameListControl.Items[i] is Game game && !string.IsNullOrWhiteSpace(game.ImageUrl))
                {
                    retainedPaths.Add(game.ImageUrl);
                }
            }

            return retainedPaths;
        }

        #endregion
    }
}
