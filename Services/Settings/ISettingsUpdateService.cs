using System.Threading.Tasks;

namespace GameLauncher.Services.Settings
{
    internal interface ISettingsUpdateService
    {
        Task CheckForUpdatesAsync();
    }
}
