using System;

namespace GameLauncher.Services
{
    internal interface ISystemUsageReader : IDisposable
    {
        HardwareStatsSnapshot ReadSnapshot();
    }
}
