using System.Windows;
using GameLauncher.Services.Localization;
using Microsoft.Win32;

namespace GameLauncher.Services.Settings
{
    internal sealed class SettingsDialogService : ISettingsDialogService
    {
        private readonly LocalizationService _localization;

        public SettingsDialogService(LocalizationService localization)
        {
            _localization = localization;
        }

        public string? SelectBackgroundImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter = _localization.Get("Settings.BackgroundDialogFilter"),
                Title = _localization.Get("Settings.BackgroundDialogTitle")
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public bool ConfirmReset() =>
            MessageBox.Show(
                _localization.Get("Settings.ResetConfirmBody"),
                _localization.Get("Settings.ResetConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
