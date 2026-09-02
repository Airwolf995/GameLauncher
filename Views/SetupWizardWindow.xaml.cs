using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameLauncher.Models;
using GameLauncher.Services.Scanners;
using GameLauncher.Services.Localization;
using GameLauncher.Services.GameManagement;

namespace GameLauncher
{
    public partial class SetupWizardWindow : Window
    {
        private const int TotalSteps = 5;

        private int _currentStep = 1;
        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private readonly Services.UISettingsService _uiSettingsService = new();
        private bool _libraryPathsDetected;
        private string _selectedLanguageCode = "en";

        public SetupWizardWindow(GameManager gameManager)
        {
            InitializeComponent();
            _gameManager = gameManager;
            _localization.ApplyLanguageCode(_gameManager.GetConfig().UISettings.LanguageCode);
            InitializeSelections();

            SourceInitialized += (s, e) => Services.DarkModeHelper.EnableDarkTitleBar(this);
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                await UpdateStepVisibilityAsync();
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepVisibility();
            }
        }


        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            CompleteWizard();
        }

        private void UpdateStepVisibility()
        {
            _ = UpdateStepVisibilityAsync();
        }

        private async Task UpdateStepVisibilityAsync()
        {
            // Toggle Content
            Step1.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
            Step5.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

            if (_currentStep == 3 && !_libraryPathsDetected)
            {
                await DetectLibraryPathsAsync();
            }

            // Update Indicators
            UpdateIndicator(Step1Indicator, _currentStep == 1);
            UpdateIndicator(Step2Indicator, _currentStep == 2);
            UpdateIndicator(Step3Indicator, _currentStep == 3);
            UpdateIndicator(Step4Indicator, _currentStep == 4);
            UpdateIndicator(Step5Indicator, _currentStep == 5);

            // Navigation buttons
            BtnBack.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Hidden;
            BtnNext.Visibility = _currentStep < TotalSteps ? Visibility.Visible : Visibility.Collapsed;
            BtnFinish.Visibility = _currentStep == TotalSteps ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateIndicator(TextBlock indicator, bool isActive)
        {
            if (isActive)
            {
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                indicator.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                indicator.FontWeight = FontWeights.Normal;
            }
        }


        private void ApplySettings()
        {
            string? selectedCardSize = (CardSizeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _gameManager.UpdateConfig(config =>
            {
                config.SteamLibraryPaths = Services.PathListFormatter.ParseLines(WizardSteamPathsBox.Text);
                config.EpicLibraryPaths = Services.PathListFormatter.ParseLines(WizardEpicPathsBox.Text);
                config.XboxLibraryPaths = Services.PathListFormatter.ParseLines(WizardXboxPathsBox.Text);
                config.UISettings.LanguageCode = _selectedLanguageCode;

                if (!string.IsNullOrWhiteSpace(selectedCardSize))
                {
                    config.UISettings.CardSizeString = selectedCardSize;
                }
            });

            // Theme
            if (ThemeBox.SelectedItem is ComboBoxItem themeItem)
            {
                _gameManager.SetTheme(Constants.UI.NormalizeThemeKey(themeItem.Tag?.ToString() ?? "Blue"));
            }

        }

        private void CompleteWizard()
        {
            _gameManager.UpdateConfig(config => config.UISettings.FirstStart = false);
            _gameManager.SaveConfig();
            this.DialogResult = true;
            this.Close();
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeBox.SelectedItem is ComboBoxItem item)
            {
                // Tag enthält bereits den HEX-Code (z.B. "#007ACC")
                string colorCode = Constants.UI.GetColorCodeForTheme(item.Tag?.ToString() ?? "Blue");

                // Über den Dienst, damit auch die Schriftfarbe auf Akzentflächen
                // mitgeführt wird - sonst bliebe sie auf hellen Akzenten unlesbar weiß.
                _uiSettingsService.ApplyTheme(colorCode);

                // Indikatoren mit neuer Farbe aktualisieren
                UpdateStepVisibility();
            }
        }

        private async Task DetectLibraryPathsAsync()
        {
            var configuredPaths = _gameManager.ReadConfig(config => (
                Steam: config.SteamLibraryPaths.ToList(),
                Epic: config.EpicLibraryPaths.ToList(),
                Xbox: config.XboxLibraryPaths.ToList()));

            LibraryDetectionStatusText.Text = _localization.Get("Wizard.LibrarySearchInProgress");
            LibraryPathInputsPanel.Visibility = Visibility.Collapsed;
            BtnNext.IsEnabled = false;
            BtnBack.IsEnabled = false;

            try
            {
                var detectedPaths = await Task.Run(() => new
                {
                    Steam = GetConfiguredOrDetectedPaths(configuredPaths.Steam, SteamScanner.GetAutoDetectedPaths),
                    Epic = GetConfiguredOrDetectedPaths(configuredPaths.Epic, EpicScanner.GetAutoDetectedPaths),
                    Xbox = GetConfiguredOrDetectedPaths(configuredPaths.Xbox, XboxScanner.GetAutoDetectedPaths),
                    Gog = GogScanner.GetAutoDetectedPaths(),
                    Ubisoft = UbisoftScanner.GetAutoDetectedPaths(),
                    Ea = EaScanner.GetAutoDetectedPaths()
                });

                WizardSteamPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Steam);
                WizardEpicPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Epic);
                WizardXboxPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Xbox);
                WizardGogPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Gog);
                WizardUbisoftPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Ubisoft);
                WizardEaPathsBox.Text = Services.PathListFormatter.FormatLines(detectedPaths.Ea);

                var statusParts = new[]
                {
                    BuildDetectionStatus("Steam", detectedPaths.Steam.Count),
                    BuildDetectionStatus("Epic", detectedPaths.Epic.Count),
                    BuildDetectionStatus("Xbox", detectedPaths.Xbox.Count),
                    BuildDetectionStatus("GOG", detectedPaths.Gog.Count),
                    BuildDetectionStatus("Ubisoft", detectedPaths.Ubisoft.Count),
                    BuildDetectionStatus("EA", detectedPaths.Ea.Count)
                };

                LibraryDetectionStatusText.Text = string.Join(" | ", statusParts);
                _libraryPathsDetected = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Library path detection in setup wizard failed", ex);
                WizardSteamPathsBox.Text = Services.PathListFormatter.FormatLines(configuredPaths.Steam);
                WizardEpicPathsBox.Text = Services.PathListFormatter.FormatLines(configuredPaths.Epic);
                WizardXboxPathsBox.Text = Services.PathListFormatter.FormatLines(configuredPaths.Xbox);
                WizardGogPathsBox.Text = string.Empty;
                WizardUbisoftPathsBox.Text = string.Empty;
                WizardEaPathsBox.Text = string.Empty;
                LibraryDetectionStatusText.Text = _localization.Get("Wizard.LibrarySearchFailed");
            }
            finally
            {
                LibraryPathInputsPanel.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = true;
                BtnBack.IsEnabled = true;
            }
        }

        private static string BuildDetectionStatus(string platform, int count) =>
            count > 0
                ? LocalizationService.Instance.Format("Wizard.DetectionFound", platform, count)
                : LocalizationService.Instance.Format("Wizard.DetectionNotFound", platform);

        private static List<string> GetConfiguredOrDetectedPaths(List<string> configuredPaths, Func<List<string>> detectPaths) =>
            configuredPaths.Count > 0 ? configuredPaths.ToList() : detectPaths();

        private void InitializeSelections()
        {
            var config = _gameManager.GetConfig();
            SetSelectedLanguage(string.Equals(config.UISettings.LanguageCode, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en");
            SelectComboBoxItemByTag(ThemeBox, Constants.UI.NormalizeThemeKey(config.Theme));
            SelectComboBoxItemByTag(CardSizeBox, config.UISettings.CardSizeString);
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string? expectedTag)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), expectedTag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void EnglishLanguageButton_Click(object sender, RoutedEventArgs e) =>
            SetSelectedLanguage("en");

        private void GermanLanguageButton_Click(object sender, RoutedEventArgs e) =>
            SetSelectedLanguage("de");

        private void SetSelectedLanguage(string languageCode)
        {
            _selectedLanguageCode = string.Equals(languageCode, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            _localization.ApplyLanguageCode(_selectedLanguageCode);

            bool isGerman = _selectedLanguageCode == "de";
            ApplyLanguageButtonState(EnglishLanguageButton, !isGerman);
            ApplyLanguageButtonState(GermanLanguageButton, isGerman);
        }

        private static void ApplyLanguageButtonState(Button button, bool isSelected)
        {
            button.Background = isSelected
                ? (Brush)Application.Current.Resources["AccentColor"]
                : new SolidColorBrush(Color.FromRgb(51, 51, 51));
            button.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
        }
    }
}
