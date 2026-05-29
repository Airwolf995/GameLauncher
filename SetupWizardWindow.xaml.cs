using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameLauncher.Models;
using GameLauncher.Services.Scanners;

namespace GameLauncher
{
    public partial class SetupWizardWindow : Window
    {
        private const int TotalSteps = 5;

        private int _currentStep = 1;
        private GameManager _gameManager;
        private bool _libraryPathsDetected;

        public SetupWizardWindow(GameManager gameManager)
        {
            InitializeComponent();
            _gameManager = gameManager;

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

            config.SteamLibraryPaths = ParsePathLines(WizardSteamPathsBox.Text);
            config.EpicLibraryPaths = ParsePathLines(WizardEpicPathsBox.Text);
            config.XboxLibraryPaths = ParsePathLines(WizardXboxPathsBox.Text);

            // Theme
            if (ThemeBox.SelectedItem is ComboBoxItem themeItem)
            {
                _gameManager.SetTheme(themeItem.Content.ToString() ?? "Blau");
            }

            // Card Size
            if (CardSizeBox.SelectedItem is ComboBoxItem sizeItem)
            {
                config.UISettings.CardSizeString = sizeItem.Tag?.ToString() ?? "Mittel";
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
                string colorCode = item.Tag?.ToString()
                    ?? Constants.UI.GetColorCodeForTheme(item.Content?.ToString() ?? "Blau");

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

            LibraryDetectionStatusText.Text = "Bibliotheken werden gesucht...";
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
                LibraryDetectionStatusText.Text = "Automatische Suche fehlgeschlagen. Du kannst die Pfade manuell eintragen.";
            }
            finally
            {
                LibraryPathInputsPanel.Visibility = Visibility.Visible;
                BtnNext.IsEnabled = true;
                BtnBack.IsEnabled = true;
            }
        }

        private static string BuildDetectionStatus(string platform, int count) =>
            count > 0 ? $"{platform}: {count} Pfad(e)" : $"{platform}: nichts gefunden";

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
    }
}
