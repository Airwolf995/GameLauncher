using System;

namespace GameLauncher.Services.MainWindow
{
    public interface IStatusMessageService : IDisposable
    {
        void ShowStatus(string message, int delayMs = 3000);
    }
}
