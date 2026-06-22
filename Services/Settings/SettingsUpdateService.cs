using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services.Settings
{
    internal sealed class SettingsUpdateService : ISettingsUpdateService
    {
        private const string GitHubRepository = "Airwolf995/GameLauncher";
        private readonly LocalizationService _localization;

        public SettingsUpdateService(LocalizationService localization)
        {
            _localization = localization;
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                using var updateService = new UpdateService(GitHubRepository);
                UpdateInfo? updateInfo = await updateService.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    var updateWindow = new UpdateWindow(updateService, updateInfo)
                    {
                        Owner = GetActiveWindow()
                    };
                    updateWindow.ShowDialog();
                    return;
                }

                ModernMessageWindow.Show(
                    _localization.Get("Settings.NoUpdatesBody"),
                    _localization.Get("Settings.NoUpdatesTitle"),
                    ModernMessageWindow.ModernMessageButton.OK,
                    GetActiveWindow());
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
        }

        private static Window? GetActiveWindow() =>
            Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
    }
}
