using System.Collections.Generic;
using GameLauncher.Models;

namespace GameLauncher.Services.GameManagement
{
    /// <summary>
    /// Ergebnis eines Bibliotheksscans. Neben den gefundenen Spielen werden die
    /// Plattformen gemeldet, deren Scan fehlgeschlagen ist oder das Zeitlimit
    /// überschritten hat, damit die Oberfläche eine unvollständige Bibliothek
    /// kenntlich machen kann.
    /// </summary>
    internal sealed record LibraryScanResult(List<Game> Games, IReadOnlyList<string> FailedPlatforms);
}
