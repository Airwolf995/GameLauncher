using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Settings;
using GameLauncher.ViewModels.Settings;

namespace GameLauncher.ViewModels
{
    public sealed class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization;
        private readonly Action<string> _onThemeChanged;
        private readonly Action<UISettings> _onSettingsChanged;
        private readonly IAutostartService _autostartService;
        private readonly ISettingsDialogService _dialogService;
        private readonly ISettingsUpdateService _updateService;
        private readonly IPlatformStatusService _platformStatusService;
        private bool _isInitialLoading = true;
        private bool _isCheckingUpdates;
        private string _updateButtonText = "";

        public SettingsViewModel(GameManager gameManager, Action<string> onThemeChanged, Action<UISettings> onSettingsChanged)
            : this(
                gameManager,
                onThemeChanged,
                onSettingsChanged,
                new AutostartService(),
                new SettingsDialogService(LocalizationService.Instance),
                new SettingsUpdateService(LocalizationService.Instance),
                new PlatformStatusService())
        {
        }

        internal SettingsViewModel(
            GameManager gameManager,
            Action<string> onThemeChanged,
            Action<UISettings> onSettingsChanged,
            IAutostartService autostartService,
            ISettingsDialogService dialogService,
            ISettingsUpdateService updateService,
            IPlatformStatusService platformStatusService)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _localization = LocalizationService.Instance;
            _onThemeChanged = onThemeChanged;
            _onSettingsChanged = onSettingsChanged;
            _autostartService = autostartService;
            _dialogService = dialogService;
            _updateService = updateService;
            _platformStatusService = platformStatusService;

            Appearance = new AppearanceSettingsViewModel(PreviewUiSettings, _onThemeChanged);
            Behavior = new BehaviorSettingsViewModel(_localization);
            Library = new LibrarySettingsViewModel();

            CloseCommand = new RelayCommand(CloseWindow);
            SelectBackgroundCommand = new RelayCommand(_ => SelectBackground());
            ClearBackgroundCommand = new RelayCommand(_ => ClearBackground());
            CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());

            LoadSettings();
            _localization.LanguageChanged += OnLanguageChanged;
            _isInitialLoading = false;
        }

        public AppearanceSettingsViewModel Appearance { get; }
        public BehaviorSettingsViewModel Behavior { get; }
        public LibrarySettingsViewModel Library { get; }

        public ICommand CloseCommand { get; }
        public ICommand SelectBackgroundCommand { get; }
        public ICommand ClearBackgroundCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }

        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            private set => SetProperty(ref _isCheckingUpdates, value);
        }

        public string UpdateButtonText
        {
            get => _updateButtonText;
            private set => SetProperty(ref _updateButtonText, value);
        }

        public string VersionText { get; private set; } = "";

        public void RevertPreview()
        {
            var config = _gameManager.Config;
            _localization.ApplyLanguageCode(config.UISettings.LanguageCode);

            string colorCode = Constants.UI.GetColorCodeForTheme(Constants.UI.NormalizeThemeKey(config.Theme));
            if (!string.IsNullOrEmpty(colorCode))
            {
                _onThemeChanged(colorCode);
            }

            _onSettingsChanged(CloneUiSettings(config.UISettings));
        }

        public void Dispose()
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        private void LoadSettings()
        {
            bool wasInitialLoading = _isInitialLoading;
            _isInitialLoading = true;
            try
            {
                var config = _gameManager.Config;
                Appearance.Load(config);
                Behavior.Load(
                    config.UISettings,
                    _autostartService.IsEnabled(config.UISettings.AutostartEnabled));
                Library.Load(config);
                LoadAutomaticPlatformPaths();
                UpdateLocalizedTexts();

                var version = Assembly.GetExecutingAssembly().GetName().Version;
                VersionText = version == null
                    ? "v0.0.0"
                    : $"v{version.Major}.{version.Minor}.{version.Build}";
                OnPropertyChanged(nameof(VersionText));
            }
            finally
            {
                _isInitialLoading = wasInitialLoading;
            }
        }

        private void PreviewUiSettings()
        {
            if (!_isInitialLoading)
            {
                _onSettingsChanged(BuildDraftUiSettings());
            }
        }

        private UISettings BuildDraftUiSettings()
        {
            var current = _gameManager.Config.UISettings;
            return new UISettings
            {
                CardSize = Appearance.CardSize,
                ViewMode = Appearance.ViewMode,
                LibrarySortMode = current.LibrarySortMode,
                LibraryFilter = current.LibraryFilter,
                AnimationsEnabled = Appearance.AnimationsEnabled,
                FontScale = Appearance.FontScale,
                BackgroundImage = Appearance.BackgroundImage,
                AutostartEnabled = Behavior.AutostartEnabled,
                AutoCheckUpdates = Behavior.AutoCheckUpdates,
                EncryptedSteamGridDbApiKey = current.EncryptedSteamGridDbApiKey,
                LanguageCode = Appearance.SelectedLanguageCode,
                MinimizeToTray = Behavior.MinimizeToTray,
                MinimizeOnGameStart = Behavior.MinimizeOnGameStart,
                CloseOnGameStart = Behavior.CloseOnGameStart,
                OverlayHotkeyCtrl = Behavior.OverlayHotkeyCtrl,
                OverlayHotkeyAlt = Behavior.OverlayHotkeyAlt,
                OverlayHotkeyShift = Behavior.OverlayHotkeyShift,
                OverlayHotkeyWin = Behavior.OverlayHotkeyWin,
                OverlayHotkeyKey = NormalizeHotkeyKey(Behavior.OverlayHotkeyKey),
                FirstStart = current.FirstStart
            };
        }

        private void ApplySettings()
        {
            var config = _gameManager.Config;
            var ui = config.UISettings;

            config.Theme = Appearance.SelectedTheme;
            ui.CardSize = Appearance.CardSize;
            ui.ViewMode = Appearance.ViewMode;
            ui.LanguageCode = Appearance.SelectedLanguageCode;
            ui.AnimationsEnabled = Appearance.AnimationsEnabled;
            ui.FontScale = Appearance.FontScale;
            ui.BackgroundImage = Appearance.BackgroundImage;
            ui.AutostartEnabled = Behavior.AutostartEnabled;
            ui.MinimizeToTray = Behavior.MinimizeToTray;
            ui.MinimizeOnGameStart = Behavior.MinimizeOnGameStart;
            ui.CloseOnGameStart = Behavior.CloseOnGameStart;
            ui.OverlayHotkeyCtrl = Behavior.OverlayHotkeyCtrl;
            ui.OverlayHotkeyAlt = Behavior.OverlayHotkeyAlt;
            ui.OverlayHotkeyShift = Behavior.OverlayHotkeyShift;
            ui.OverlayHotkeyWin = Behavior.OverlayHotkeyWin;
            ui.OverlayHotkeyKey = NormalizeHotkeyKey(Behavior.OverlayHotkeyKey);
            ui.AutoCheckUpdates = Behavior.AutoCheckUpdates;
            ui.SteamGridDbApiKey = Library.SteamGridDbApiKey;

            config.IgnoredProcesses = PathListFormatter.ParseLines(Library.IgnoredProcessesText);
            config.SteamLibraryPaths = PathListFormatter.ParseLines(Library.SteamPathsText);
            config.EpicLibraryPaths = PathListFormatter.ParseLines(Library.EpicPathsText);
            config.XboxLibraryPaths = PathListFormatter.ParseLines(Library.XboxPathsText);

            _localization.ApplyLanguageCode(ui.LanguageCode);
            _autostartService.SetEnabled(Behavior.AutostartEnabled);
        }

        private void CloseWindow(object? parameter)
        {
            if (parameter is not Window window)
            {
                return;
            }

            ApplySettings();
            _gameManager.SaveConfig();
            window.DialogResult = true;
            window.Close();
        }

        private void SelectBackground()
        {
            string? selectedPath = _dialogService.SelectBackgroundImage();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                Appearance.BackgroundImage = selectedPath;
                PreviewUiSettings();
            }
        }

        private void ClearBackground()
        {
            Appearance.BackgroundImage = "";
            PreviewUiSettings();
        }

        private async Task CheckUpdatesAsync()
        {
            try
            {
                IsCheckingUpdates = true;
                UpdateButtonText = _localization.Get("Settings.Checking");
                await _updateService.CheckForUpdatesAsync();
            }
            finally
            {
                UpdateButtonText = _localization.Get("Settings.CheckUpdatesNow");
                IsCheckingUpdates = false;
            }
        }

        private void ResetToDefaults()
        {
            if (!_dialogService.ConfirmReset())
            {
                return;
            }

            Appearance.Reset();
            Behavior.Reset();
            PreviewUiSettings();

            string colorCode = Constants.UI.GetColorCodeForTheme(Appearance.SelectedTheme);
            if (!string.IsNullOrEmpty(colorCode))
            {
                _onThemeChanged(colorCode);
            }
        }

        private void LoadAutomaticPlatformPaths()
        {
            Library.GogPathsText = PathListFormatter.FormatLines(_platformStatusService.GetGogLibraryPaths());
            Library.UbisoftPathsText = PathListFormatter.FormatLines(_platformStatusService.GetUbisoftLibraryPaths());
            Library.EaPathsText = PathListFormatter.FormatLines(_platformStatusService.GetEaLibraryPaths());
        }

        private void UpdateLocalizedTexts()
        {
            UpdateButtonText = _localization.Get("Settings.CheckUpdatesNow");
            Behavior.RefreshLocalizedTexts();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            LoadAutomaticPlatformPaths();
            UpdateLocalizedTexts();
        }

        private static string NormalizeHotkeyKey(string key) => string.IsNullOrWhiteSpace(key) ? "G" : key;

        private static UISettings CloneUiSettings(UISettings settings) =>
            new()
            {
                CardSizeString = settings.CardSizeString,
                ViewModeString = settings.ViewModeString,
                LibrarySortModeString = settings.LibrarySortModeString,
                LibraryFilter = settings.LibraryFilter,
                AnimationsEnabled = settings.AnimationsEnabled,
                FontScale = settings.FontScale,
                BackgroundImage = settings.BackgroundImage,
                AutostartEnabled = settings.AutostartEnabled,
                AutoCheckUpdates = settings.AutoCheckUpdates,
                EncryptedSteamGridDbApiKey = settings.EncryptedSteamGridDbApiKey,
                LanguageCode = settings.LanguageCode,
                MinimizeToTray = settings.MinimizeToTray,
                MinimizeOnGameStart = settings.MinimizeOnGameStart,
                CloseOnGameStart = settings.CloseOnGameStart,
                OverlayHotkeyCtrl = settings.OverlayHotkeyCtrl,
                OverlayHotkeyAlt = settings.OverlayHotkeyAlt,
                OverlayHotkeyShift = settings.OverlayHotkeyShift,
                OverlayHotkeyWin = settings.OverlayHotkeyWin,
                OverlayHotkeyKey = settings.OverlayHotkeyKey,
                FirstStart = settings.FirstStart
            };
    }
}
