using System.Collections.Generic;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Services.Settings
{
    internal sealed class PlatformStatusService : IPlatformStatusService
    {
        public IReadOnlyList<string> GetGogLibraryPaths() => GogScanner.GetAutoDetectedPaths();
        public IReadOnlyList<string> GetUbisoftLibraryPaths() => UbisoftScanner.GetAutoDetectedPaths();
        public IReadOnlyList<string> GetEaLibraryPaths() => EaScanner.GetAutoDetectedPaths();
    }
}
