using System.Linq;
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
            ModernMessageWindow.Show(
                _localization.Get("Settings.ResetConfirmBody"),
                _localization.Get("Settings.ResetConfirmTitle"),
                ModernMessageWindow.ModernMessageButton.YesNo,
                Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault()) == MessageBoxResult.Yes;
    }
}
