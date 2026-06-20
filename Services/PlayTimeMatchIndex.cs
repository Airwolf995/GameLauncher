using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    public sealed class PlayTimeMatchIndex
    {
        private readonly Dictionary<string, List<Game>> _gamesByExecutableName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<InstallPathIndexEntry> _installPathEntries = new();
        private readonly Dictionary<string, Game> _gamesById = new(StringComparer.Ordinal);

        public void Rebuild(IEnumerable<Game> games)
        {
            _gamesByExecutableName.Clear();
            _installPathEntries.Clear();
            _gamesById.Clear();

            foreach (var game in games)
            {
                if (game == null || string.IsNullOrWhiteSpace(game.Id))
                {
                    continue;
                }

                _gamesById[game.Id] = game;

                // Skip manual games for playtime tracking
                if (game.IsManual)
                {
                    continue;
                }

                var exeName = game.ExecutableName;
                if (string.IsNullOrWhiteSpace(exeName) && game.LaunchType == "exe" && !string.IsNullOrWhiteSpace(game.Path))
                {
                    try
                    {
                        exeName = Path.GetFileName(game.Path);
                    }
                    catch
                    {
                        // Ignoriere Fehler bei der Pfad-Analyse
                    }
                }

                if (!string.IsNullOrWhiteSpace(exeName))
                {
                    var executableName = Path.GetFileName(exeName.Trim());
                    if (!string.IsNullOrWhiteSpace(executableName))
                    {
                        if (!_gamesByExecutableName.TryGetValue(executableName, out var gameList))
                        {
                            gameList = new List<Game>();
                            _gamesByExecutableName[executableName] = gameList;
                        }

                        gameList.Add(game);
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(game.InstallDirectory))
                {
                    continue;
                }

                foreach (var rawPath in game.InstallDirectory.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var normalizedPath = NormalizePath(rawPath);
                    if (string.IsNullOrEmpty(normalizedPath))
                    {
                        continue;
                    }

                    _installPathEntries.Add(new InstallPathIndexEntry(
                        ExactPath: normalizedPath,
                        PrefixWithSlash: normalizedPath + "\\",
                        Game: game));
                }
            }

            foreach (var executableGroup in _gamesByExecutableName.Values)
            {
                executableGroup.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            }

            _installPathEntries.Sort((a, b) => b.PrefixWithSlash.Length.CompareTo(a.PrefixWithSlash.Length));
        }

        public bool TryMatchProcessByName(string processName, out string gameId)
        {
            gameId = string.Empty;

            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            var normalizedProcessName = Path.GetFileName(processName);
            if (_gamesByExecutableName.TryGetValue(normalizedProcessName, out var executableMatches) &&
                executableMatches.Count > 0)
            {
                gameId = executableMatches[0].Id;
                return true;
            }

            // Wenn der Prozessname keine Endung hat, aber der Index-Key .exe enthält
            if (!normalizedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var nameWithExe = normalizedProcessName + ".exe";
                if (_gamesByExecutableName.TryGetValue(nameWithExe, out var exeMatches) &&
                    exeMatches.Count > 0)
                {
                    gameId = exeMatches[0].Id;
                    return true;
                }
            }

            return false;
        }

        public bool TryMatchProcess(string processName, string processPath, out string gameId)
        {
            gameId = string.Empty;

            var normalizedProcessName = Path.GetFileName(processName);
            if (!string.IsNullOrWhiteSpace(normalizedProcessName) &&
                _gamesByExecutableName.TryGetValue(normalizedProcessName, out var executableMatches) &&
                executableMatches.Count > 0)
            {
                gameId = executableMatches[0].Id;
                return true;
            }

            foreach (var entry in _installPathEntries)
            {
                if (string.Equals(processPath, entry.ExactPath, StringComparison.OrdinalIgnoreCase) ||
                    processPath.StartsWith(entry.PrefixWithSlash, StringComparison.OrdinalIgnoreCase))
                {
                    gameId = entry.Game.Id;
                    return true;
                }
            }

            return false;
        }

        public Game? GetGameById(string gameId)
        {
            return _gamesById.TryGetValue(gameId, out var game) ? game : null;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return path.Replace('/', '\\').Trim().TrimEnd('\\');
            }
            catch
            {
                return path;
            }
        }

        private sealed record InstallPathIndexEntry(string ExactPath, string PrefixWithSlash, Game Game);
    }
}
