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
        public async Task<List<Game>> LoadAsync(GameConfig config, CancellationToken cancellationToken)
        {
            Logger.Log("Starting scanning games parallel...");

            var steamTask = new SteamScanner(config.SteamLibraryPaths).ScanAsync(cancellationToken);
            var gogTask = new GogScanner().ScanAsync(cancellationToken);
            var epicTask = new EpicScanner(config.EpicLibraryPaths).ScanAsync(cancellationToken);
            var eaTask = new EaScanner().ScanAsync(cancellationToken);
            var xboxTask = new XboxScanner(config.XboxLibraryPaths).ScanAsync(cancellationToken);

            await Task.WhenAll(steamTask, gogTask, epicTask, eaTask, xboxTask);

            Logger.Log(
                $"Parallel scan finished. Steam: {steamTask.Result.Count}, GOG: {gogTask.Result.Count}, " +
                $"Epic: {epicTask.Result.Count}, EA: {eaTask.Result.Count}, Xbox: {xboxTask.Result.Count}");

            var games = new List<Game>();
            games.AddRange(steamTask.Result);
            games.AddRange(gogTask.Result);
            games.AddRange(epicTask.Result);
            games.AddRange(eaTask.Result);
            games.AddRange(xboxTask.Result);
            AddManualGames(config, games);
            return games;
        }

        public async Task<List<Game>> LoadDeferredAsync(CancellationToken cancellationToken)
        {
            var games = await new UbisoftScanner().ScanAsync(cancellationToken);
            Logger.Log($"Zeitversetzter Startup-Scan abgeschlossen. Ubisoft: {games.Count}");
            return games;
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
    }
}
