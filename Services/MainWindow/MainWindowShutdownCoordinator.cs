using System;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    internal sealed class MainWindowShutdownCoordinator
    {
        public bool ExitRequested { get; private set; }
        public bool IsPrepared { get; private set; }
        public bool IsPreparing { get; private set; }

        public void RequestExit() => ExitRequested = true;

        public bool ShouldMinimizeToTray(UISettings settings) =>
            !ExitRequested && settings.MinimizeToTray;

        public bool TryBeginPreparation()
        {
            if (IsPrepared || IsPreparing)
            {
                return false;
            }

            IsPreparing = true;
            return true;
        }

        public async Task PrepareAsync(PlayTimeService? playTimeService)
        {
            try
            {
                if (playTimeService != null)
                {
                    await playTimeService.StopAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Spielzeiterfassung konnte nicht kontrolliert beendet werden", ex);
            }
            finally
            {
                IsPrepared = true;
                IsPreparing = false;
            }
        }
    }
}
