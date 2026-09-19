using System;
using System.ComponentModel;
using System.Diagnostics;
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
        private readonly ISensorSourceProbe _sensorSourceProbe;
        private readonly IConfigTransferService _configTransferService;
        private bool _isInitialLoading = true;
        private bool _isCheckingUpdates;
        private string _updateButtonText = "";

        /// <summary>
        /// Das Ergebnis der Erreichbarkeitspruefung: <c>null</c>, solange sie
        /// laeuft. Der unbekannte Zustand ist noetig, weil die Pruefung im
        /// Hintergrund stattfindet - ohne ihn zeigte das Fenster bis zu zwei
        /// Sekunden lang eine bestehende Verbindung an, die es nie gab.
        /// </summary>
        private bool? _isSensorSourceAvailable;

        public SettingsViewModel(GameManager gameManager, Action<string> onThemeChanged, Action<UISettings> onSettingsChanged)
            : this(
                gameManager,
                onThemeChanged,
                onSettingsChanged,
                new AutostartService(),
                new SettingsDialogService(LocalizationService.Instance),
                new SettingsUpdateService(LocalizationService.Instance),
                new PlatformStatusService(),
                new SensorSourceProbe(),
                new ConfigTransferService(gameManager.ConfigService))
        {
        }

        internal SettingsViewModel(
            GameManager gameManager,
            Action<string> onThemeChanged,
            Action<UISettings> onSettingsChanged,
            IAutostartService autostartService,
            ISettingsDialogService dialogService,
            ISettingsUpdateService updateService,
            IPlatformStatusService platformStatusService,
            ISensorSourceProbe sensorSourceProbe,
            IConfigTransferService configTransferService)
        {
            _gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            _localization = LocalizationService.Instance;
            _onThemeChanged = onThemeChanged;
            _onSettingsChanged = onSettingsChanged;
            _autostartService = autostartService;
            _dialogService = dialogService;
            _updateService = updateService;
            _platformStatusService = platformStatusService;
            _sensorSourceProbe = sensorSourceProbe;
            _configTransferService = configTransferService;

            Appearance = new AppearanceSettingsViewModel(PreviewUiSettings, _onThemeChanged);
            Behavior = new BehaviorSettingsViewModel(_localization);
            Library = new LibrarySettingsViewModel();

            CloseCommand = new RelayCommand(CloseWindow);
            SelectBackgroundCommand = new RelayCommand(_ => SelectBackground());
            ClearBackgroundCommand = new RelayCommand(_ => ClearBackground());
            CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());
            ExportConfigCommand = new RelayCommand(_ => ExportConfig());
            ImportConfigCommand = new RelayCommand(_ => ImportConfig());
            OpenSensorSourceCommand = new RelayCommand(_ => OpenSensorSourcePage());
            // Waehrend einer laufenden Pruefung ist der Knopf abgeblendet. Das ist
            // nicht nur Kosmetik: Es verhindert, dass zwei Abfragen nebeneinander
            // laufen und eine langsame alte Antwort die neue ueberschreibt.
            RecheckSensorSourceCommand = new RelayCommand(
                _ => CheckSensorSourceAvailability(),
                _ => !IsSensorSourceChecking);

            LoadSettings();
            CheckSensorSourceAvailability();
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
        public ICommand ExportConfigCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand OpenSensorSourceCommand { get; }
        public ICommand RecheckSensorSourceCommand { get; }

        /// <summary>
        /// Meldet, dass keine LibreHardwareMonitor-Anwendung erreichbar ist. Nur
        /// dann erscheint der Hinweis in den Einstellungen - laeuft sie, gibt es
        /// nichts zu melden.
        /// </summary>
        public bool IsSensorSourceMissing => _isSensorSourceAvailable == false;

        public bool IsSensorSourceConnected => _isSensorSourceAvailable == true;

        /// <summary>
        /// Meldet, dass die Erreichbarkeit gerade geprueft wird.
        /// </summary>
        public bool IsSensorSourceChecking => _isSensorSourceAvailable == null;

        /// <summary>
        /// Die laufende Pruefung. Sie laeuft im Hintergrund und meldet ihr
        /// Ergebnis von selbst an die Anzeige; nur Tests muessen abwarten
        /// koennen, bis sie durch ist.
        /// </summary>
        internal Task SensorSourceCheck { get; private set; } = Task.CompletedTask;

        private void SetSensorSourceAvailability(bool? isAvailable)
        {
            if (_isSensorSourceAvailable == isAvailable)
            {
                return;
            }

            _isSensorSourceAvailable = isAvailable;
            OnPropertyChanged(nameof(IsSensorSourceMissing));
            OnPropertyChanged(nameof(IsSensorSourceConnected));
            OnPropertyChanged(nameof(IsSensorSourceChecking));

            // Der Knopf "Erneut pruefen" ist waehrend der Pruefung abgeblendet.
            // Ohne diesen Anstoss fragt WPF erst bei der naechsten Eingabe nach,
            // ob er wieder benutzbar ist - er bliebe also abgeblendet stehen,
            // bis der Benutzer das Fenster zufaellig beruehrt.
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Die Pruefung ruft die Anwendung ueber HTTP ab und wartet dabei bis zu
        /// zwei Sekunden, deshalb laeuft sie im Hintergrund: das
        /// Einstellungsfenster soll sofort erscheinen. Bis das Ergebnis vorliegt,
        /// weist die Anzeige die Pruefung aus, statt eine der beiden Antworten
        /// vorwegzunehmen.
        /// </summary>
        private void CheckSensorSourceAvailability()
        {
            SetSensorSourceAvailability(null);

            SensorSourceCheck = Task.Run(() =>
            {
                bool isAvailable = _sensorSourceProbe.IsAvailable();
                Action applyResult = () => SetSensorSourceAvailability(isAvailable);
                applyResult.RunOnUI();
            });
        }

        private void OpenSensorSourcePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Services.LibreHardwareMonitorWebSource.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Downloadseite von LibreHardwareMonitor konnte nicht geoeffnet werden", ex);
            }
        }

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
            string languageCode = Appearance.SelectedLanguageCode;
            _gameManager.UpdateConfig(config =>
            {
                var ui = config.UISettings;
                config.Theme = Appearance.SelectedTheme;
                ui.CardSize = Appearance.CardSize;
                ui.ViewMode = Appearance.ViewMode;
                ui.LanguageCode = languageCode;
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
            });

            _localization.ApplyLanguageCode(languageCode);
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

        /// <summary>
        /// Sichert den aktuellen Stand in eine frei gewählte Datei. Gesichert
        /// wird die Konfiguration mitsamt Spielzeiten, Favoriten, Schlagwörtern
        /// und manuellen Einträgen.
        /// </summary>
        private void ExportConfig()
        {
            string? targetPath = _dialogService.SelectConfigExportTarget(
                $"GameLauncher-Konfiguration-{DateTime.Now:yyyy-MM-dd}.json");
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            var result = _configTransferService.Export(targetPath);
            ShowTransferResult(
                result == ConfigTransferResult.Success
                    ? "Settings.ExportConfigSucceeded"
                    : "Settings.ExportConfigFailed",
                "Settings.ExportConfigTitle");
        }

        /// <summary>
        /// Spielt eine gesicherte Konfiguration ein. Sie ersetzt den bisherigen
        /// Stand vollständig und wird erst mit dem nächsten Start wirksam, da die
        /// laufende Anwendung noch die bisherige Konfiguration im Speicher führt.
        /// </summary>
        private void ImportConfig()
        {
            string? sourcePath = _dialogService.SelectConfigImportSource();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            if (!_dialogService.ConfirmImport())
            {
                return;
            }

            var result = _configTransferService.Import(sourcePath);
            ShowTransferResult(
                result switch
                {
                    ConfigTransferResult.Success => "Settings.ImportConfigSucceeded",
                    ConfigTransferResult.NotAConfigFile => "Settings.ImportConfigNotAConfigFile",
                    ConfigTransferResult.SourceUnreadable => "Settings.ImportConfigUnreadable",
                    _ => "Settings.ImportConfigFailed"
                },
                "Settings.ImportConfigTitle");
        }

        private void ShowTransferResult(string messageKey, string titleKey) =>
            _dialogService.ShowConfigTransferResult(
                _localization.Get(messageKey),
                _localization.Get(titleKey));

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
