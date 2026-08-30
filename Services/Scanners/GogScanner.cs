using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans GOG Galaxy registry for installed games.
    /// </summary>
    public class GogScanner : IPlatformScanner
    {
        public string PlatformName => "GOG";

        private const string GamesRegistryPath = @"SOFTWARE\GOG.com\Games";

        public static List<string> GetAutoDetectedPaths()
        {
            var paths = new List<string>();

            RegistryScanUtility.ForEachSubKey(GamesRegistryPath, (_, gameKey) =>
            {
                string? workingDirectory = gameKey.GetValue("workingDir") as string;
                string? executable = gameKey.GetValue("exe") as string;
                string? installDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.ExpandEnvironmentVariables(workingDirectory)
                    : Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(executable ?? string.Empty));

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

            RegistryScanUtility.ForEachSubKey(GamesRegistryPath, (subKeyName, gameKey) =>
            {
                ct.ThrowIfCancellationRequested();

                string? gameName = gameKey.GetValue("gameName") as string;
                string? exePath = gameKey.GetValue("exe") as string;
                string? workingDir = gameKey.GetValue("workingDir") as string;

                if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(exePath))
                {
                    return;
                }

                // Expand environment variables
                exePath = Environment.ExpandEnvironmentVariables(exePath);

                if (!File.Exists(exePath))
                {
                    Logger.Log($"GOG game exe not found: {exePath}");
                    return;
                }

                games.Add(new Game
                {
                    Id = $"gog_{subKeyName}",
                    Name = gameName,
                    Path = exePath,
                    Args = "",
                    Platform = "GOG",
                    LaunchType = "exe",
                    ImageUrl = IconExtractor.GetIconFromExe(exePath, $"gog_{subKeyName}"),
                    InstallDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? ""
                });
                Logger.Log($"Found GOG game: {gameName}");
            });

            return games;
        }
    }
}
