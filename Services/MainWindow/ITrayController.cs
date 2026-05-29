using System;

namespace GameLauncher.Services.MainWindow
{
    public interface ITrayController : IDisposable
    {
        void Initialize(Action onOpenRequested, Action onExitRequested);
        void ShowTrayIcon();
        void HideTrayIcon();
        void ShowBalloon(string title, string text, int timeoutMs);
    }
}
