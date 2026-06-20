using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Interface for platform-specific game scanners.
    /// Each platform (Steam, GOG, Epic) implements this interface.
    /// </summary>
    public interface IPlatformScanner
    {
        /// <summary>
        /// The name of the platform this scanner handles.
        /// </summary>
        string PlatformName { get; }

        /// <summary>
        /// Scans for installed games on this platform.
        /// </summary>
        /// <returns>List of discovered games.</returns>
        Task<List<Game>> ScanAsync(CancellationToken ct = default);
    }
}
