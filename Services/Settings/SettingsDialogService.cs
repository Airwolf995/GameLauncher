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
                OwnerWindow) == MessageBoxResult.Yes;

        public string? SelectConfigExportTarget(string suggestedFileName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = _localization.Get("Settings.ConfigTransferDialogFilter"),
                Title = _localization.Get("Settings.ExportConfigDialogTitle"),
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".json"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? SelectConfigImportSource()
        {
            var dialog = new OpenFileDialog
            {
                Filter = _localization.Get("Settings.ConfigTransferDialogFilter"),
                Title = _localization.Get("Settings.ImportConfigDialogTitle"),
                CheckFileExists = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public bool ConfirmImport() =>
            ModernMessageWindow.Show(
                _localization.Get("Settings.ImportConfigConfirmBody"),
                _localization.Get("Settings.ImportConfigConfirmTitle"),
                ModernMessageWindow.ModernMessageButton.YesNo,
                OwnerWindow) == MessageBoxResult.Yes;

        public bool ConfirmRestartAfterImport() =>
            ModernMessageWindow.Show(
                _localization.Get("Settings.ImportConfigRestartBody"),
                _localization.Get("Settings.ImportConfigTitle"),
                ModernMessageWindow.ModernMessageButton.YesNo,
                OwnerWindow) == MessageBoxResult.Yes;

        public void ShowConfigTransferResult(string message, string title) =>
            ModernMessageWindow.Show(
                message,
                title,
                ModernMessageWindow.ModernMessageButton.OK,
                OwnerWindow);

        private static SettingsWindow? OwnerWindow =>
            Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault();
    }
}
