using System;
using System.Collections.Generic;
using System.IO;
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

            var steamTask = ScanPlatformAsync("Steam", () => new SteamScanner(config.SteamLibraryPaths), cancellationToken);
            var gogTask = ScanPlatformAsync("GOG", () => new GogScanner(), cancellationToken);
            var epicTask = ScanPlatformAsync("Epic", () => new EpicScanner(config.EpicLibraryPaths), cancellationToken);
            var eaTask = ScanPlatformAsync("EA", () => new EaScanner(), cancellationToken);
            var xboxTask = ScanPlatformAsync("Xbox", () => new XboxScanner(config.XboxLibraryPaths), cancellationToken);

            await Task.WhenAll(steamTask, gogTask, epicTask, eaTask, xboxTask);

            var results = new[] { steamTask.Result, gogTask.Result, epicTask.Result, eaTask.Result, xboxTask.Result };

            Logger.Log(
                $"Parallel scan finished. Steam: {steamTask.Result.Games.Count}, GOG: {gogTask.Result.Games.Count}, " +
                $"Epic: {epicTask.Result.Games.Count}, EA: {eaTask.Result.Games.Count}, Xbox: {xboxTask.Result.Games.Count}");

            var games = new List<Game>();
            var failedPlatforms = new List<string>();
            foreach (var result in results)
            {
                games.AddRange(result.Games);
                failedPlatforms.AddRange(result.FailedPlatforms);
            }

            AddManualGames(config, games);
            return new LibraryScanResult(games, failedPlatforms);
        }

        public async Task<LibraryScanResult> LoadDeferredAsync(CancellationToken cancellationToken)
        {
            var result = await ScanPlatformAsync("Ubisoft", () => new UbisoftScanner(), cancellationToken);
            Logger.Log($"Zeitversetzter Startup-Scan abgeschlossen. Ubisoft: {result.Games.Count}");
            return result;
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

        internal static async Task<LibraryScanResult> ScanPlatformAsync(
            string platformName,
            Func<IPlatformScanner> createScanner,
            CancellationToken cancellationToken)
        {
            try
            {
                var games = await createScanner()
                    .ScanAsync(cancellationToken)
                    .WaitAsync(ScanTimeout, cancellationToken);
                return new LibraryScanResult(games, []);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                Logger.Error(
                    $"{platformName}-Scanner hat das Zeitlimit von {ScanTimeout.TotalSeconds:0} Sekunden überschritten; " +
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
