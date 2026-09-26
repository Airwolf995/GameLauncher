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

                // Manuelle Spiele wurden früher pauschal übergangen. Sie besitzen mit
                // ihrem Programmpfad dieselbe Grundlage wie die Spiele der Plattformen
                // und werden daher mitgeführt; ausgenommen bleiben Einträge ohne
                // zuordenbaren Prozess.
                if (!game.SupportsPlayTimeTracking)
                {
                    continue;
                }

                // Startet der Eintrag über einen Launcher, ist dessen Programmdatei
                // nicht das Spiel. Ihr Name würde den dauerhaft laufenden Client
                // als Spielsitzung erfassen.
                bool startsViaLauncher = Constants.Launchers.IsLauncherExecutable(game.Path);

                var exeName = game.ExecutableName;
                if (string.IsNullOrWhiteSpace(exeName) && game.LaunchType == "exe" &&
                    !startsViaLauncher && !string.IsNullOrWhiteSpace(game.Path))
                {
                    exeName = Path.GetFileName(game.Path);
                }

                if (!string.IsNullOrWhiteSpace(exeName))
                {
                    var executableName = NormalizeExecutableName(exeName);
                    if (!string.IsNullOrWhiteSpace(executableName))
                    {
                        if (!_gamesByExecutableName.TryGetValue(executableName, out var gameList))
                        {
                            gameList = new List<Game>();
                            _gamesByExecutableName[executableName] = gameList;
                        }

                        gameList.Add(game);
                    }
                }

                // Bei einem manuellen Eintrag auf einen Launcher ist das
                // Installationsverzeichnis aus dessen Pfad abgeleitet, also der
                // Ordner des Clients mit dessen Prozessen. Würde er beobachtet,
                // zählte jeder Client-Prozess auf das Spiel. Die Zuordnung bleibt
                // dann allein über den Prozessnamen möglich, den der Benutzer in
                // ExecutableName hinterlegen kann. Plattform-Scanner wie Battle.net
                // liefern dagegen den echten Spielordner.
                if (string.IsNullOrWhiteSpace(game.InstallDirectory) ||
                    (game.IsManual && startsViaLauncher))
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

            var normalizedProcessName = NormalizeExecutableName(processName);
            if (_gamesByExecutableName.TryGetValue(normalizedProcessName, out var executableMatches) &&
                executableMatches.Count == 1)
            {
                gameId = executableMatches[0].Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ordnet einen Prozess über seinen Programmpfad dem Spiel zu, in dessen
        /// Installationsverzeichnis er liegt. Bei verschachtelten Verzeichnissen
        /// gewinnt das längste, weil die Einträge danach sortiert sind.
        /// </summary>
        public bool TryMatchProcessByPath(string processPath, out string gameId)
        {
            gameId = string.Empty;

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

            return path.Replace('/', '\\').Trim().TrimEnd('\\');
        }

        private static string NormalizeExecutableName(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileName(executableName.Trim());
            return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^4]
                : fileName;
        }

        private sealed record InstallPathIndexEntry(string ExactPath, string PrefixWithSlash, Game Game);
    }
}
