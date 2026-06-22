using System.Collections.Generic;

namespace GameLauncher.Services.Settings
{
    internal interface IPlatformStatusService
    {
        IReadOnlyList<string> GetGogLibraryPaths();
        IReadOnlyList<string> GetUbisoftLibraryPaths();
        IReadOnlyList<string> GetEaLibraryPaths();
    }
}
