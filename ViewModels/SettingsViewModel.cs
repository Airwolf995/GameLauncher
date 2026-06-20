using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;
using Microsoft.Win32;

namespace GameLauncher.ViewModels
{
    public class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization;
        private readonly Action<string> _onThemeChanged;
        private readonly Action<UISettings> _onSettingsChanged;
        private bool _isInitialLoading = true;
        public IEnumerable<Models.CardSize> CardSizeOptions => Enum.GetValues(typeof(Models.CardSize)).Cast<Models.CardSize>();
        public IEnumerable<Models.ViewMode> ViewModeOptions => Enum.GetValues(typeof(Models.ViewMode)).Cast<Models.ViewMode>();
        
        // Settings Properties
        private string _selectedTheme = "";
        private Models.CardSize _cardSize;
        private Models.ViewMode _viewMode;
        private bool _animationsEnabled;
        private bool _autostartEnabled;
        private bool _minimizeToTray;
        private bool _minimizeOnGameStart;
        private bool _closeOnGameStart;
        private bool _overlayHotkeyCtrl;
        private bool _overlayHotkeyAlt;
        private bool _overlayHotkeyShift;
        private bool _overlayHotkeyWin;
        private string _overlayHotkeyKey = "";
        private double _fontScale;
        private bool _autoCheckUpdates;
        private string _steamGridDbApiKey = "";
        private string _ignoredProcessesText = "";
        private string _steamPathsText = "";
        private string _epicPathsText = "";
        private string _xboxPathsText = "";
        private string _gogPathsText = "";
        private string _versionText = "";
        private string _backgroundImage = "";
        private string _selectedLanguageCode = "en";

        public SettingsViewModel(GameManager gameManager, Action<string> onThemeChanged, Action<UISettings> onSettingsChanged)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _localization = LocalizationService.Instance;
            _onThemeChanged = onThemeChanged;
            _onSettingsChanged = onSettingsChanged;

            // Initialize Commands
            CloseCommand = new RelayCommand(CloseWindow);
            SelectBackgroundCommand = new RelayCommand(_ => SelectBackground());
            ClearBackgroundCommand = new RelayCommand(_ => ClearBackground());
            CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());

            LoadSettings();
            _localization.LanguageChanged += OnLanguageChanged;
            _isInitialLoading = false;
        }

        // Commands
        public ICommand CloseCommand { get; }
        public ICommand SelectBackgroundCommand { get; }
        public ICommand ClearBackgroundCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }

        public IReadOnlyList<string> OverlayHotkeyKeys { get; } = BuildOverlayHotkeyKeys();

        private bool _isCheckingUpdates;
        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set => SetProperty(ref _isCheckingUpdates, value);
        }

        private string _updateButtonText = "";
        public string UpdateButtonText
        {
            get => _updateButtonText;
            set => SetProperty(ref _updateButtonText, value);
        }

        // Properties
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                {
                    string colorCode = Constants.UI.GetColorCodeForTheme(value);
                    if (!string.IsNullOrEmpty(colorCode))
                    {
                        _onThemeChanged?.Invoke(colorCode);
                    }
                }
            }
        }

        public Models.CardSize CardSize
        {
            get => _cardSize;
            set
            {
                if (SetProperty(ref _cardSize, value))
                {
                    PreviewUiSettings();
                }
            }
        }

        public string SelectedLanguageCode
        {
            get => _selectedLanguageCode;
            set
            {
                var normalized = string.Equals(value, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
                SetProperty(ref _selectedLanguageCode, normalized);
            }
        }

        public Models.ViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    PreviewUiSettings();
                }
            }
        }

        public bool AnimationsEnabled
        {
            get => _animationsEnabled;
            set
            {
                if (SetProperty(ref _animationsEnabled, value))
                {
                    PreviewUiSettings();
                }
            }
        }

        public bool AutostartEnabled
        {
            get => _autostartEnabled;
            set
            {
                if (SetProperty(ref _autostartEnabled, value))
                {
                }
            }
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                if (SetProperty(ref _minimizeToTray, value))
                {
                }
            }
        }

        public bool MinimizeOnGameStart
        {
            get => _minimizeOnGameStart;
            set
            {
                if (SetProperty(ref _minimizeOnGameStart, value))
                {
                    if (!_isInitialLoading)
                    {
                        if (value && CloseOnGameStart)
                        {
                            CloseOnGameStart = false; 
                        }

                    }
                }
            }
        }

        public bool CloseOnGameStart
        {
            get => _closeOnGameStart;
            set
            {
                if (SetProperty(ref _closeOnGameStart, value))
                {
                    if (!_isInitialLoading)
                    {
                        if (value && MinimizeOnGameStart)
                        {
                            MinimizeOnGameStart = false; 
                        }

                    }
                }
            }
        }

        public double FontScale
        {
            get => _fontScale;
            set
            {
                if (SetProperty(ref _fontScale, value))
                {
                    PreviewUiSettings();
                    OnPropertyChanged(nameof(FontScalePercentage));
                }
            }
        }

        public string FontScalePercentage => $"{(int)(FontScale * 100)}%";

        public bool OverlayHotkeyCtrl
        {
            get => _overlayHotkeyCtrl;
            set
            {
                if (SetProperty(ref _overlayHotkeyCtrl, value))
                {
                    SaveOverlayHotkeySettings();
                }
            }
        }

        public bool OverlayHotkeyAlt
        {
            get => _overlayHotkeyAlt;
            set
            {
                if (SetProperty(ref _overlayHotkeyAlt, value))
                {
                    SaveOverlayHotkeySettings();
                }
            }
        }

        public bool OverlayHotkeyShift
        {
            get => _overlayHotkeyShift;
            set
            {
                if (SetProperty(ref _overlayHotkeyShift, value))
                {
                    SaveOverlayHotkeySettings();
                }
            }
        }

        public bool OverlayHotkeyWin
        {
            get => _overlayHotkeyWin;
            set
            {
                if (SetProperty(ref _overlayHotkeyWin, value))
                {
                    SaveOverlayHotkeySettings();
                }
            }
        }

        public string OverlayHotkeyKey
        {
            get => _overlayHotkeyKey;
            set
            {
                if (SetProperty(ref _overlayHotkeyKey, value))
                {
                    SaveOverlayHotkeySettings();
                }
            }
        }

        public string OverlayHotkeyDisplay => BuildOverlayHotkeyDisplay();
        public string OverlayHotkeyDisplayText => _localization.Format("Settings.CurrentHotkey", BuildOverlayHotkeyDisplay());

        public bool AutoCheckUpdates
        {
            get => _autoCheckUpdates;
            set
            {
                if (SetProperty(ref _autoCheckUpdates, value))
                {
                }
            }
        }

        public string SteamGridDbApiKey
        {
            get => _steamGridDbApiKey;
            set
            {
                if (SetProperty(ref _steamGridDbApiKey, value))
                {
                }
            }
        }

        public string IgnoredProcessesText
        {
            get => _ignoredProcessesText;
            set
            {
                if (SetProperty(ref _ignoredProcessesText, value))
                {
                }
            }
        }

        public string SteamPathsText
        {
            get => _steamPathsText;
            set
            {
                if (SetProperty(ref _steamPathsText, value) && !_isInitialLoading)
                {
                }
            }
        }

        public string EpicPathsText
        {
            get => _epicPathsText;
            set
            {
                if (SetProperty(ref _epicPathsText, value) && !_isInitialLoading)
                {
                }
            }
        }

        public string XboxPathsText
        {
            get => _xboxPathsText;
            set
            {
                if (SetProperty(ref _xboxPathsText, value) && !_isInitialLoading)
                {
                }
            }
        }

        public string GogPathsText
        {
            get => _gogPathsText;
            set => SetProperty(ref _gogPathsText, value);
        }

        public string VersionText
        {
            get => _versionText;
            set => SetProperty(ref _versionText, value);
        }

        // Methods
        private void LoadSettings()
        {
            bool wasInitialLoading = _isInitialLoading;
            _isInitialLoading = true;

            var config = _gameManager.GetConfig();
            var ui = config.UISettings;

            // Load values
            _selectedTheme = Constants.UI.NormalizeThemeKey(config.Theme);
            _selectedLanguageCode = string.Equals(ui.LanguageCode, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            _cardSize = ui.CardSize;
            _viewMode = ui.ViewMode;
            _animationsEnabled = ui.AnimationsEnabled;
            // Autostart Checked Real Status
            _autostartEnabled = CheckAutostartRegistry(ui.AutostartEnabled);
            _minimizeToTray = ui.MinimizeToTray;
            _minimizeOnGameStart = ui.MinimizeOnGameStart;
            _closeOnGameStart = ui.CloseOnGameStart;
            _overlayHotkeyCtrl = ui.OverlayHotkeyCtrl;
            _overlayHotkeyAlt = ui.OverlayHotkeyAlt;
            _overlayHotkeyShift = ui.OverlayHotkeyShift;
            _overlayHotkeyWin = ui.OverlayHotkeyWin;
            _overlayHotkeyKey = string.IsNullOrWhiteSpace(ui.OverlayHotkeyKey) ? "G" : ui.OverlayHotkeyKey;
            _fontScale = ui.FontScale > 0 ? ui.FontScale : 1.0;
            _autoCheckUpdates = ui.AutoCheckUpdates;
            _steamGridDbApiKey = ui.SteamGridDbApiKey ?? "";
            _backgroundImage = ui.BackgroundImage ?? "";
            
            // Text Fields
            if (config.IgnoredProcesses != null)
                _ignoredProcessesText = string.Join(Environment.NewLine, config.IgnoredProcesses);
                
            SteamPathsText = FormatPathLines(config.SteamLibraryPaths);
            EpicPathsText = FormatPathLines(config.EpicLibraryPaths);
            XboxPathsText = FormatPathLines(config.XboxLibraryPaths);

            LoadGogStatus();
            UpdateLocalizedTexts();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText = version != null
                ? $"v{version.Major}.{version.Minor}.{version.Build}"
                : "v0.0.0";

            // Trigger UI refresh for all properties
            OnPropertyChanged(string.Empty);
            _isInitialLoading = wasInitialLoading;
        }

        private static List<string> ParsePathLines(string value) =>
            value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(line => line.Trim())
                 .Where(line => !string.IsNullOrWhiteSpace(line))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

        private static string FormatPathLines(IEnumerable<string> paths) =>
            string.Join(Environment.NewLine, paths);

        private void PreviewUiSettings()
        {
            if (_isInitialLoading)
            {
                return;
            }

            _onSettingsChanged?.Invoke(BuildDraftUiSettings());
        }

        public void RevertPreview()
        {
            var config = _gameManager.GetConfig();
            _localization.ApplyLanguageCode(config.UISettings.LanguageCode);

            string colorCode = Constants.UI.GetColorCodeForTheme(Constants.UI.NormalizeThemeKey(config.Theme));
            if (!string.IsNullOrEmpty(colorCode))
            {
                _onThemeChanged?.Invoke(colorCode);
            }

            _onSettingsChanged?.Invoke(CloneUiSettings(config.UISettings));
        }

        private UISettings BuildDraftUiSettings()
        {
            var current = _gameManager.GetConfig().UISettings;
            return new UISettings
            {
                CardSize = CardSize,
                ViewMode = ViewMode,
                LibrarySortMode = current.LibrarySortMode,
                LibraryFilter = current.LibraryFilter,
                AnimationsEnabled = AnimationsEnabled,
                FontScale = FontScale,
                BackgroundImage = _backgroundImage,
                AutostartEnabled = AutostartEnabled,
                AutoCheckUpdates = AutoCheckUpdates,
                EncryptedSteamGridDbApiKey = current.EncryptedSteamGridDbApiKey,
                LanguageCode = SelectedLanguageCode,
                MinimizeToTray = MinimizeToTray,
                MinimizeOnGameStart = MinimizeOnGameStart,
                CloseOnGameStart = CloseOnGameStart,
                OverlayHotkeyCtrl = OverlayHotkeyCtrl,
                OverlayHotkeyAlt = OverlayHotkeyAlt,
                OverlayHotkeyShift = OverlayHotkeyShift,
                OverlayHotkeyWin = OverlayHotkeyWin,
                OverlayHotkeyKey = string.IsNullOrWhiteSpace(OverlayHotkeyKey) ? "G" : OverlayHotkeyKey,
                FirstStart = current.FirstStart
            };
        }

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

        private bool CheckAutostartRegistry(bool configSetting)
        {
            try
            {
                bool isActuallyEnabled = false;
                using (RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (runKey?.GetValue("GameLauncher") != null)
                    {
                        isActuallyEnabled = true;
                         using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", false))
                         {
                             if (approvedKey != null)
                             {
                                 byte[]? approvedValue = approvedKey.GetValue("GameLauncher") as byte[];
                                 if (approvedValue != null && approvedValue.Length > 0)
                                 {
                                     isActuallyEnabled = (approvedValue[0] == 0x02);
                                }
                            }
                        }
                    }
                }
                return isActuallyEnabled;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to check Autostart registry", ex);
                return configSetting;
            }
        }

        private void UpdateAutostartRegistry(bool enabled)
        {
             try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        string? appPath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(appPath))
                        {
                            return;
                        }

                        if (enabled)
                        {
                            key.SetValue("GameLauncher", $"\"{appPath}\"");
                        }
                        else
                        {
                            key.DeleteValue("GameLauncher", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update Autostart registry", ex);
            }
        }

        private void LoadGogStatus()
        {
            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
                {
                    if (key != null)
                    {
                        int count = 0;
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey? gameKey = key.OpenSubKey(subKeyName))
                            {
                                if (gameKey != null)
                                {
                                    string? exe = gameKey.GetValue("exe") as string;
                                    if (!string.IsNullOrEmpty(exe) && File.Exists(Environment.ExpandEnvironmentVariables(exe)))
                                    {
                                        count++;
                                    }
                                }
                            }
                        }
                        GogPathsText = _localization.Format("Settings.GogDetected", count);
                    }
                    else
                    {
                        GogPathsText = _localization.Get("Settings.GogRegistryMissing");
                    }
                }
            }
            catch 
            {
                 GogPathsText = _localization.Get("Settings.GogRegistryError"); 
            }
        }

        private void SelectBackground()
        {
            var dialog = new OpenFileDialog
            {
                Filter = _localization.Get("Settings.BackgroundDialogFilter"),
                Title = _localization.Get("Settings.BackgroundDialogTitle")
            };

            if (dialog.ShowDialog() == true)
            {
                _backgroundImage = dialog.FileName;
                PreviewUiSettings();
            }
        }

        private void ClearBackground()
        {
            _backgroundImage = "";
            PreviewUiSettings();
        }

        private async Task CheckUpdatesAsync()
        {
             try
            {
                IsCheckingUpdates = true;
                UpdateButtonText = _localization.Get("Settings.Checking");

                var updateService = new UpdateService("Airwolf995/GameLauncher");
                var updateInfo = await updateService.CheckForUpdatesAsync();

                if (updateInfo != null)
                {
                    var updateWindow = new UpdateWindow(updateService, updateInfo);
                    updateWindow.ShowDialog();
                }
                else
                {
                    ModernMessageWindow.Show(
                        _localization.Get("Settings.NoUpdatesBody"),
                        _localization.Get("Settings.NoUpdatesTitle"),
                        ModernMessageWindow.ModernMessageButton.OK,
                        GetActiveWindow());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Manual update check failed", ex);
                ModernMessageWindow.Show(
                    _localization.Get("Settings.UpdateErrorBody"),
                    _localization.Get("Common.Error"),
                    ModernMessageWindow.ModernMessageButton.OK,
                    GetActiveWindow());
            }
            finally
            {
                UpdateButtonText = _localization.Get("Settings.CheckUpdatesNow");
                IsCheckingUpdates = false;
            }
        }

        private void CloseWindow(object? param)
        {
            if (param is Window window)
            {
                ApplySettings();
                _gameManager.SaveConfig();
                window.DialogResult = true;
                window.Close();
            }
        }

        private void ApplySettings()
        {
            var config = _gameManager.GetConfig();
            var ui = config.UISettings;

            config.Theme = SelectedTheme;
            ui.CardSize = CardSize;
            ui.ViewMode = ViewMode;
            ui.LanguageCode = SelectedLanguageCode;
            _localization.ApplyLanguageCode(ui.LanguageCode);
            ui.AnimationsEnabled = AnimationsEnabled;
            ui.AutostartEnabled = AutostartEnabled;
            ui.MinimizeToTray = MinimizeToTray;
            ui.MinimizeOnGameStart = MinimizeOnGameStart;
            ui.CloseOnGameStart = CloseOnGameStart;
            ui.FontScale = FontScale;
            ui.OverlayHotkeyCtrl = OverlayHotkeyCtrl;
            ui.OverlayHotkeyAlt = OverlayHotkeyAlt;
            ui.OverlayHotkeyShift = OverlayHotkeyShift;
            ui.OverlayHotkeyWin = OverlayHotkeyWin;
            ui.OverlayHotkeyKey = string.IsNullOrWhiteSpace(OverlayHotkeyKey) ? "G" : OverlayHotkeyKey;
            ui.AutoCheckUpdates = AutoCheckUpdates;
            ui.SteamGridDbApiKey = SteamGridDbApiKey;
            ui.BackgroundImage = _backgroundImage;

            config.IgnoredProcesses = ParsePathLines(IgnoredProcessesText);
            config.SteamLibraryPaths = ParsePathLines(SteamPathsText);
            config.EpicLibraryPaths = ParsePathLines(EpicPathsText);
            config.XboxLibraryPaths = ParsePathLines(XboxPathsText);

            UpdateAutostartRegistry(AutostartEnabled);
        }

        private void ResetToDefaults()
        {
            if (MessageBox.Show(_localization.Get("Settings.ResetConfirmBody"), 
                _localization.Get("Settings.ResetConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SelectedTheme = "Blue";
                SelectedLanguageCode = "en";
                CardSize = Models.CardSize.Medium;
                ViewMode = Models.ViewMode.Cards;
                AnimationsEnabled = true;
                FontScale = 1.0;
                AutoCheckUpdates = true;
                MinimizeToTray = false;
                MinimizeOnGameStart = false;
                CloseOnGameStart = false;
                OverlayHotkeyCtrl = false;
                OverlayHotkeyAlt = true;
                OverlayHotkeyShift = false;
                OverlayHotkeyWin = false;
                OverlayHotkeyKey = "G";
                _backgroundImage = "";
                PreviewUiSettings();
                
                string colorCode = Constants.UI.GetColorCodeForTheme("Blue");
                if (!string.IsNullOrEmpty(colorCode))
                {
                    _onThemeChanged?.Invoke(colorCode);
                }
            }
        }

        private void SaveOverlayHotkeySettings()
        {
            if (!_overlayHotkeyCtrl && !_overlayHotkeyAlt && !_overlayHotkeyShift && !_overlayHotkeyWin)
            {
                _overlayHotkeyAlt = true;
                OnPropertyChanged(nameof(OverlayHotkeyAlt));
            }

            OnPropertyChanged(nameof(OverlayHotkeyDisplay));
            OnPropertyChanged(nameof(OverlayHotkeyDisplayText));
        }

        private string BuildOverlayHotkeyDisplay()
        {
            var parts = new List<string>();

            if (OverlayHotkeyCtrl) parts.Add(_localization.CurrentLanguage == AppLanguage.German ? "Strg" : "Ctrl");
            if (OverlayHotkeyAlt) parts.Add("Alt");
            if (OverlayHotkeyShift) parts.Add("Shift");
            if (OverlayHotkeyWin) parts.Add("Win");

            parts.Add(string.IsNullOrWhiteSpace(OverlayHotkeyKey) ? "G" : OverlayHotkeyKey);
            return string.Join("+", parts);
        }

        private static IReadOnlyList<string> BuildOverlayHotkeyKeys()
        {
            var keys = new List<string>();

            for (char c = 'A'; c <= 'Z'; c++)
            {
                keys.Add(c.ToString());
            }

            for (int i = 0; i <= 9; i++)
            {
                keys.Add(i.ToString());
            }

            for (int i = 1; i <= 12; i++)
            {
                keys.Add($"F{i}");
            }

            return keys;
        }

        private static Window? GetActiveWindow() =>
            Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;

        public void Dispose()
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        private void UpdateLocalizedTexts()
        {
            UpdateButtonText = _localization.Get("Settings.CheckUpdatesNow");
            OnPropertyChanged(nameof(OverlayHotkeyDisplay));
            OnPropertyChanged(nameof(OverlayHotkeyDisplayText));
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            LoadGogStatus();
            UpdateLocalizedTexts();
        }
    }
}
