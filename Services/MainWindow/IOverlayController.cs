using System;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Services.MainWindow
{
    public interface IOverlayController : IDisposable
    {
        void Initialize(Window owner, PlayTimeService playTimeService);
        void RegisterHotkey(Window owner, UISettings settings);
        void Stop();
    }
}
