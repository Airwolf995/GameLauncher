using System;
using System.Threading.Tasks;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Services.MainWindow
{
    public sealed class UpdateCoordinator : IUpdateCoordinator
    {
        private readonly UpdateService _updateService;

        public UpdateCoordinator(string githubRepo)
        {
            if (githubRepo == null) throw new ArgumentNullException(nameof(githubRepo));
            _updateService = new UpdateService(githubRepo);
        }

        public async Task CheckForUpdatesAsync(Window owner)
        {
            try
            {
                var updateInfo = await _updateService.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    return;
                }

                await owner.Dispatcher.InvokeAsync(() =>
                {
                    var updateWindow = new UpdateWindow(_updateService, updateInfo)
                    {
                        Owner = owner
                    };
                    updateWindow.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Update check failed", ex);
                // Fail silently to avoid interrupting main workflows.
            }
        }

        public void Dispose() => _updateService.Dispose();
    }
}
