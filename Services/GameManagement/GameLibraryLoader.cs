using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Services.GameManagement
{
    internal sealed class GameLibraryLoader
    {
        /// <summary>
        /// Zeitlimit je Plattform. Ein Scanner, der auf ein nicht erreichbares
        /// Laufwerk oder einen blockierten Registry-Zugriff wartet, darf das Laden
        /// der übrigen Bibliothek nicht unbegrenzt aufhalten.
        /// </summary>
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(60);

        public async Task<LibraryScanResult> LoadAsync(GameConfig config, CancellationToken cancellationToken)
        {
            Logger.Log("Starting scanning games parallel...");

            var steamTask = ScanPlatformAsync(Constants.Platforms.Steam, () => new SteamScanner(config.SteamLibraryPaths), cancellationToken);
            var gogTask = ScanPlatformAsync(Constants.Platforms.GOG, () => new GogScanner(), cancellationToken);
            var epicTask = ScanPlatformAsync(Constants.Platforms.Epic, async token =>
            {
                var (games, hasDirectoryErrors) = await new EpicScanner(config.EpicLibraryPaths).ScanWithStatusAsync(token);
                return new LibraryScanResult(games, hasDirectoryErrors ? [Constants.Platforms.Epic] : []);
            }, cancellationToken);
            var eaTask = ScanPlatformAsync(Constants.Platforms.EAApp, () => new EaScanner(), cancellationToken);
            var xboxTask = ScanPlatformAsync(Constants.Platforms.Xbox, () => new XboxScanner(config.XboxLibraryPaths), cancellationToken);
            var battleNetTask = ScanPlatformAsync(Constants.Platforms.BattleNet, () => new BattleNetScanner(), cancellationToken);

            var ubisoftTask = ScanPlatformAsync(Constants.Platforms.UbisoftConnect, () => new UbisoftScanner(), cancellationToken);
            var results = await Task.WhenAll(steamTask, gogTask, epicTask, eaTask, xboxTask, battleNetTask, ubisoftTask);

            Logger.Log(
                $"Parallel scan finished. Steam: {steamTask.Result.Games.Count}, GOG: {gogTask.Result.Games.Count}, " +
                $"Epic: {epicTask.Result.Games.Count}, EA: {eaTask.Result.Games.Count}, Xbox: {xboxTask.Result.Games.Count}, " +
                $"Battle.net: {battleNetTask.Result.Games.Count}, Ubisoft: {ubisoftTask.Result.Games.Count}");

            var games = new List<Game>();
            // Ubisoft bleibt bei gleicher Spiel-ID hinter den bisherigen Einträgen.
            foreach (var result in results.AsSpan(0, results.Length - 1))
            {
                games.AddRange(result.Games);
            }

            AddManualGames(config, games);
            if (ubisoftTask.Result.Games.Count > 0)
            {
                games.AddRange(ubisoftTask.Result.Games);
                games = games.DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return new LibraryScanResult(games, results.SelectMany(result => result.FailedPlatforms).ToList());
        }

        private static void AddManualGames(GameConfig config, ICollection<Game> games)
        {
            foreach (var game in config.ManualGames)
            {
                game.IsManual = true;

                if (string.IsNullOrEmpty(game.ImageUrl) && game.LaunchType == "exe" && File.Exists(game.Path))
                {
                    game.ImageUrl = IconExtractor.GetIconFromExe(game.Path, game.Id);
                }

                if (string.IsNullOrEmpty(game.InstallDirectory) &&
                    !string.IsNullOrEmpty(game.Path) &&
                    game.LaunchType == "exe")
                {
                    game.InstallDirectory = Path.GetDirectoryName(game.Path) ?? "";
                }

                games.Add(game);
            }

            Logger.Log($"Loaded {config.ManualGames.Count} manual games.");
        }

        internal static Task<LibraryScanResult> ScanPlatformAsync(
            string platformName,
            Func<IPlatformScanner> createScanner,
            CancellationToken cancellationToken,
            TimeSpan? scanTimeout = null) =>
            ScanPlatformAsync(platformName, async token => new LibraryScanResult(await createScanner().ScanAsync(token), []),
                cancellationToken, scanTimeout);

        private static async Task<LibraryScanResult> ScanPlatformAsync(
            string platformName,
            Func<CancellationToken, Task<LibraryScanResult>> scan,
            CancellationToken cancellationToken,
            TimeSpan? scanTimeout = null)
        {
            // Das Zeitlimit ist für gezielte Tests verkürzbar; im Betrieb bleibt
            // es bei 60 Sekunden pro Plattform.
            TimeSpan timeout = scanTimeout ?? ScanTimeout;
            using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken scanToken = scanCancellation.Token;
            try
            {
                // Auch das Erzeugen im Hintergrund: Die Konstruktoren erkennen
                // ohne eingetragene Pfade die Bibliotheken selbst, und der
                // Aufrufer ist beim Start der Oberflächen-Thread.
                return await Task.Run(() => scan(scanToken), scanToken)
                    .WaitAsync(timeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // WaitAsync kann vor dem verknüpften Abbruch-Callback fortsetzen.
                // Das Signal muss vor dem Freigeben der Quelle beim Scanner ankommen.
                scanCancellation.Cancel();
                throw;
            }
            catch (TimeoutException)
            {
                // WaitAsync beendet nur das Warten. Der Scanner muss zusätzlich
                // bei seiner nächsten Abbruchprüfung von der Überschreitung erfahren.
                scanCancellation.Cancel();
                Logger.Error(
                    $"{platformName}-Scanner hat das Zeitlimit von {timeout.TotalSeconds:0} Sekunden überschritten; " +
                    "die Plattform wird für diesen Durchlauf übersprungen.");
                return new LibraryScanResult([], [platformName]);
            }
            catch (Exception ex)
            {
                Logger.Error($"{platformName}-Scanner fehlgeschlagen; die übrigen Plattformen werden weiter geladen.", ex);
                return new LibraryScanResult([], [platformName]);
            }
        }
    }
}
