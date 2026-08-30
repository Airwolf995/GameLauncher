using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Ubisoft Connect registry for installed games.
    /// </summary>
    public class UbisoftScanner : IPlatformScanner
    {
        public string PlatformName => Constants.Platforms.UbisoftConnect;

        private const string InstallsRegistryPath = @"SOFTWARE\Ubisoft\Launcher\Installs";

        public static List<string> GetAutoDetectedPaths()
        {
            var paths = new List<string>();

            RegistryScanUtility.ForEachSubKey(InstallsRegistryPath, (_, gameKey) =>
            {
                string? installDirectory = gameKey.GetValue("InstallDir") as string;
                if (!string.IsNullOrWhiteSpace(installDirectory))
                {
                    ScannerPathUtility.AddExistingDirectory(paths, installDirectory);
                }
            });

            return ScannerPathUtility.GetLibraryDirectories(paths);
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            // Die Registry kennt nur Installationsverzeichnisse; die Titel stehen
            // in der Konfigurationsdatei des Launchers.
            var catalogNames = UbisoftGameNameCatalog.Load();

            // Ubisoft Connect speichert die Installationen in der Registry
            RegistryScanUtility.ForEachSubKey(InstallsRegistryPath, (gameIdStr, gameKey) =>
            {
                ct.ThrowIfCancellationRequested();

                string? installDir = gameKey.GetValue("InstallDir") as string;
                if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                {
                    return;
                }

                // Ohne Katalogeintrag bleibt der Ordnername die beste Näherung.
                if (!catalogNames.TryGetValue(gameIdStr, out string? gameName) ||
                    string.IsNullOrWhiteSpace(gameName))
                {
                    gameName = new DirectoryInfo(installDir).Name;
                }

                if (string.IsNullOrWhiteSpace(gameName))
                {
                    gameName = gameIdStr;
                }

                string exePath = ExecutableSelector.FindPrimaryExecutable(
                    installDir,
                    "unins", "crash", "redist");
                string iconUrl = "";

                if (!string.IsNullOrEmpty(exePath))
                {
                    iconUrl = IconExtractor.GetIconFromExe(exePath, $"ubi_{gameIdStr}");
                }

                games.Add(new Game
                {
                    Id = $"ubi_{gameIdStr}",
                    Name = gameName,
                    Path = $"uplay://launch/{gameIdStr}/0",
                    Args = "",
                    Platform = Constants.Platforms.UbisoftConnect,
                    LaunchType = "uri",
                    ImageUrl = iconUrl,
                    InstallDirectory = installDir
                });
                Logger.Log($"Found Ubisoft game: {gameName} (ID: {gameIdStr})");
            });

            return games;
        }

    }
}
