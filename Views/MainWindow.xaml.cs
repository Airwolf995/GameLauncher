using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.Models;
using GameLauncher.Core;
using GameLauncher.Services.Localization;
using GameLauncher.Services.GameManagement;

namespace GameLauncher
{
    using GameLauncher.ViewModels;

    /// <summary>
    /// Lebenszyklus des Hauptfensters: Aufbau, Infobereich, Beenden und die
    /// Befehle der Kopfleiste. Bibliotheksdarstellung und Spielaktionen liegen
    /// in MainWindow.LibraryView.cs und MainWindow.GameActions.cs.
    /// </summary>
    public partial class MainWindow : Window
    {
        private GameManager _gameManager = null!;
        private MainViewModel _viewModel = null!;
        private DataTemplate? _originalCardTemplate; // Store original XAML template

        private Services.PlayTimeService _playTimeService = null!;
        private Services.UISettingsService _uiSettingsService = null!;
        private Services.MainWindow.GameCardLayoutService _gameCardLayoutService = null!;
        private Services.MainWindow.TrayController _trayController = null!;
        private Services.MainWindow.OverlayController _overlayController = null!;
        private Services.MainWindow.StatusMessageService _statusMessageService = null!;
        private Services.MainWindow.UpdateCoordinator _updateCoordinator = null!;
        private Services.MainWindow.MainWindowStartupCoordinator _startupCoordinator = null!;
        private readonly Services.MainWindow.MainWindowShutdownCoordinator _shutdownCoordinator = new();
        private Services.MainWindow.AnimationService _animationService = null!;
        private readonly LocalizationService _localization = LocalizationService.Instance;

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

        public MainWindow()
        {
            InitializeComponent();

            ClampSizeToWorkArea();

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
                _gameManager?.Dispose();
                throw new InvalidOperationException(
                    _localization.Format("App.GameManagerInitError", ex.Message),
                    ex);
            }
            
            // Zeilen-Template fuer den stabilen, virtualisierten Kartenmodus merken.
            _originalCardTemplate = GameListControl.ItemTemplate;

            ContentRendered += MainWindow_ContentRendered;

            _uiSettingsService = new Services.UISettingsService();
            _gameCardLayoutService = new Services.MainWindow.GameCardLayoutService(_uiSettingsService);
            _animationService = new Services.MainWindow.AnimationService();
            _trayController = new Services.MainWindow.TrayController();
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
            _startupCoordinator = new Services.MainWindow.MainWindowStartupCoordinator(
                _gameManager,
                _viewModel,
                _updateCoordinator,
                _localization);
            _localization.LanguageChanged += OnLanguageChanged;

            ApplySavedTheme();
        }

        internal void InitializeRuntimeServices()
        {
            _playTimeService = new Services.PlayTimeService(_gameManager, _viewModel.Games);
            _playTimeService.Start();
            _overlayController.Initialize(this, _playTimeService);
        }

        private void OnGamesUpdatedInWindow(object? sender, EventArgs e)
        {
            // Trigger visual refresh (animation/stats) when data updates
            new Action(() => RefreshList(instant: true)).RunOnUI();
        }

        internal void InitializeTrayIcon()
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

        private void BeginExit()
        {
            _shutdownCoordinator.RequestExit();
        }

        internal void ExitApplication()
        {
            BeginExit();
            _trayController?.Dispose();
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            var gameManager = _gameManager;
            var config = gameManager?.GetConfig();

            if (config != null && _shutdownCoordinator.ShouldMinimizeToTray(config.UISettings))
            {
                if (gameManager != null && config != null)
                {
                    gameManager.SaveConfigImmediate(config);
                }

                e.Cancel = true;
                Hide();
                _trayController?.ShowTrayIcon();
                _trayController?.ShowBalloon(_localization.Get("Main.TrayMinimizedTitle"), _localization.Get("Main.TrayMinimizedBody"), Constants.Timings.TrayBalloonDurationMs);
            }
            else
            {
                if (!_shutdownCoordinator.IsPrepared)
                {
                    e.Cancel = true;
                    if (_shutdownCoordinator.TryBeginPreparation())
                    {
                        _ = PrepareShutdownAsync();
                    }
                    return;
                }

                // OnClosing wird beim vorbereiteten Herunterfahren ein zweites Mal aufgerufen.
                // Erst in diesem finalen Durchlauf speichern, damit der letzte Spielzeit-Tick
                // enthalten ist und die Konfiguration nicht doppelt geschrieben wird.
                if (gameManager != null && config != null)
                {
                    gameManager.SaveConfigImmediate(config);
                }

                if (_gameManager != null) _gameManager.GamesUpdated -= OnGamesUpdatedInWindow;
                _playTimeService?.Dispose();
                _overlayController?.Dispose();
                _trayController?.Dispose();
                _statusMessageService?.Dispose();
                _updateCoordinator?.Dispose();
                if (_viewModel != null) _viewModel.LibraryViewRefreshed -= OnLibraryViewRefreshed;
                _viewModel?.Dispose();
                _gameManager?.Dispose();
                _localization.LanguageChanged -= OnLanguageChanged;
                base.OnClosing(e);
            }
        }

        private async Task PrepareShutdownAsync()
        {
            await _shutdownCoordinator.PrepareAsync(_playTimeService);
            await Task.Yield();
            Close();
        }

        private async void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;
            await _startupCoordinator.RunAsync(this);
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

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = _localization.Get("AppName");
        }

        /// <summary>
        /// Die Mindestgröße hält das Layout der Kopfleiste zusammen, liegt aber
        /// deutlich unter der Startgröße, damit das Fenster auch auf kleinen
        /// Bildschirmen verkleinert werden kann. Auf einer sehr kleinen
        /// Arbeitsfläche - etwa Full HD bei 150 % Skalierung - würde die
        /// Startgröße über den Bildschirmrand hinausragen, weshalb die Werte
        /// hier zusätzlich auf die verfügbare Fläche begrenzt werden.
        /// </summary>
        private void ClampSizeToWorkArea()
        {
            var workArea = SystemParameters.WorkArea;

            MinWidth = Math.Min(MinWidth, workArea.Width);
            MinHeight = Math.Min(MinHeight, workArea.Height);
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
        }

        private void MoreActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget = button;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow(_gameManager, _uiSettingsService.ApplyTheme, ApplyUiSettingsPreview);
            settings.Owner = this;
            if (settings.ShowDialog() == true)
            {
                // Apply UI settings immediately without restart
                ApplySavedUiSettings();
            }
        }

        private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (IsInitialLoading)
            {
                return;
            }

            try
            {
                IsInitialLoading = true;
                await _viewModel.LoadGamesAsync(includeDeferredStartupGames: true);
                RefreshList(instant: false);
            }
            catch (Exception ex)
            {
                Logger.Error("Manual library refresh failed", ex);
                ModernMessageWindow.Show(_localization.Format("App.LoadError", ex.Message), _localization.Get("Common.Error"), ModernMessageWindow.ModernMessageButton.OK, this);
            }
            finally
            {
                IsInitialLoading = false;
            }
        }

        private void ShowStatus(string message, int delayMs = 3000) =>
            _statusMessageService.ShowStatus(message, delayMs);
    }
}
