using System;
using System.Threading.Tasks;
using System.Windows;

namespace GameLauncher.Services.MainWindow
{
    public interface IUpdateCoordinator : IDisposable
    {
        Task CheckForUpdatesAsync(Window owner);
    }
}
