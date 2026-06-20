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

namespace GameLauncher
{
    public partial class SetupWizardWindow : Window
    {
        private const int TotalSteps = 5;

        private int _currentStep = 1;
        private readonly GameManager _gameManager;
        private readonly LocalizationService _localization = LocalizationService.Instance;
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
            var config = _gameManager.GetConfig();
            var ui = config.UISettings;

            config.SteamLibraryPaths = ParsePathLines(WizardSteamPathsBox.Text);
            config.EpicLibraryPaths = ParsePathLines(WizardEpicPathsBox.Text);
            config.XboxLibraryPaths = ParsePathLines(WizardXboxPathsBox.Text);
            ui.LanguageCode = _selectedLanguageCode;

            // Theme
            if (ThemeBox.SelectedItem is ComboBoxItem themeItem)
            {
                _gameManager.SetTheme(Constants.UI.NormalizeThemeKey(themeItem.Tag?.ToString() ?? "Blue"));
            }

            // Card Size
            if (CardSizeBox.SelectedItem is ComboBoxItem sizeItem)
            {
                ui.CardSizeString = sizeItem.Tag?.ToString() ?? "Medium";
            }
        }

        private void CompleteWizard()
        {
            _gameManager.GetConfig().UISettings.FirstStart = false;
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

                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorCode);
                    var brush = new SolidColorBrush(color);
                    Application.Current.Resources["AccentColor"] = brush;

                    // Indikatoren mit neuer Farbe aktualisieren
                    UpdateStepVisibility();
                }
                catch { }
            }
        }

        private async Task DetectLibraryPathsAsync()
        {
            var config = _gameManager.GetConfig();

            LibraryDetectionStatusText.Text = _localization.Get("Wizard.LibrarySearchInProgress");
            LibraryPathInputsPanel.Visibility = Visibility.Collapsed;
            BtnNext.IsEnabled = false;
            BtnBack.IsEnabled = false;

            try
            {
                var detectedPaths = await Task.Run(() => new
                {
                    Steam = GetConfiguredOrDetectedPaths(config.SteamLibraryPaths, SteamScanner.GetAutoDetectedPaths),
                    Epic = GetConfiguredOrDetectedPaths(config.EpicLibraryPaths, EpicScanner.GetAutoDetectedPaths),
                    Xbox = GetConfiguredOrDetectedPaths(config.XboxLibraryPaths, XboxScanner.GetAutoDetectedPaths)
                });

                WizardSteamPathsBox.Text = FormatPathLines(detectedPaths.Steam);
                WizardEpicPathsBox.Text = FormatPathLines(detectedPaths.Epic);
                WizardXboxPathsBox.Text = FormatPathLines(detectedPaths.Xbox);

                var statusParts = new[]
                {
                    BuildDetectionStatus("Steam", detectedPaths.Steam.Count),
                    BuildDetectionStatus("Epic", detectedPaths.Epic.Count),
                    BuildDetectionStatus("Xbox", detectedPaths.Xbox.Count)
                };

                LibraryDetectionStatusText.Text = string.Join(" | ", statusParts);
                _libraryPathsDetected = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Library path detection in setup wizard failed", ex);
                WizardSteamPathsBox.Text = FormatPathLines(config.SteamLibraryPaths);
                WizardEpicPathsBox.Text = FormatPathLines(config.EpicLibraryPaths);
                WizardXboxPathsBox.Text = FormatPathLines(config.XboxLibraryPaths);
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

        private static string FormatPathLines(IEnumerable<string> paths) =>
            string.Join(Environment.NewLine, paths);

        private static List<string> ParsePathLines(string value) =>
            value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(line => line.Trim())
                 .Where(line => !string.IsNullOrWhiteSpace(line))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

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
