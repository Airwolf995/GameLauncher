using System;

namespace GameLauncher.Services.MainWindow
{
    public interface IFpsCounter : IDisposable
    {
        void Start(Action<int> onFpsCalculated);
        void Stop();
    }
}
