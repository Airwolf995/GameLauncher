using System;
using System.Collections.Generic;
using System.Linq;
using GameLauncher.Models;
using GameLauncher.ViewModels;

namespace GameLauncher.Services
{
    internal sealed record LibraryViewSnapshot(
        List<Game> Games,
        List<GameRow> CardRows,
        IReadOnlyList<string> InitialWarmupImagePaths);

    internal static class LibraryViewSnapshotBuilder
    {
        private const int InitialWarmupRows = 8;
        private const int InitialWarmupImageLimit = 24;

        public static LibraryViewSnapshot Create(
            List<Game> games,
            string searchText,
            string selectedFilter,
            GameSortMode selectedSort,
            int columns)
        {
            var filteredGames = BuildGames(games, searchText, selectedFilter, selectedSort);
            var cardRows = BuildCardRows(filteredGames, columns);
            var initialWarmupImagePaths = BuildInitialWarmupImagePaths(filteredGames, columns);
            return new LibraryViewSnapshot(filteredGames, cardRows, initialWarmupImagePaths);
        }

        public static List<GameRow> BuildCardRows(IReadOnlyList<Game> games, int columns)
        {
            var rows = new List<GameRow>();
            int safeColumns = Math.Max(1, columns);

            for (int index = 0; index < games.Count; index += safeColumns)
            {
                int length = Math.Min(safeColumns, games.Count - index);
                var rowGames = new List<Game>(length);
                for (int offset = 0; offset < length; offset++)
                {
                    rowGames.Add(games[index + offset]);
                }

                rows.Add(new GameRow(rowGames));
            }

            return rows;
        }

        public static bool MatchesFilter(Game game, string searchText, string selectedFilter)
        {
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                bool matchesName = game.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase);
                bool matchesTag = game.Tags.Any(tag => tag.Contains(searchText, StringComparison.OrdinalIgnoreCase));

                if (!matchesName && !matchesTag)
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(selectedFilter) || selectedFilter == Constants.Filters.All)
            {
                return !game.IsHidden;
            }

            if (selectedFilter == Constants.Filters.Hidden)
            {
                return game.IsHidden;
            }

            if (game.IsHidden)
            {
                return false;
            }

            return selectedFilter switch
            {
                Constants.Filters.Favorites => game.IsFavorite,
                Constants.Filters.Manual => game.IsManual,
                Constants.Platforms.Steam => game.Platform == Constants.Platforms.Steam,
                Constants.Platforms.Epic => game.Platform == Constants.Platforms.Epic,
                "Ubisoft Connect" => game.Platform == "Ubisoft Connect",
                "EA App" => game.Platform == "EA App",
                "Xbox" => game.Platform == "Xbox",
                Constants.Platforms.GOG => game.Platform == Constants.Platforms.GOG,
                _ when selectedFilter.StartsWith(Constants.Filters.TagPrefix, StringComparison.Ordinal) =>
                    game.Tags.Contains(selectedFilter.Substring(Constants.Filters.TagPrefix.Length)),
                _ => true
            };
        }

        private static List<Game> BuildGames(
            List<Game> games,
            string searchText,
            string selectedFilter,
            GameSortMode selectedSort)
        {
            IEnumerable<Game> filteredGames = games.Where(game => MatchesFilter(game, searchText, selectedFilter));

            return selectedSort switch
            {
                GameSortMode.Favorites => filteredGames
                    .OrderByDescending(game => game.IsFavorite)
                    .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                GameSortMode.LastPlayed => filteredGames
                    .OrderByDescending(game => game.LastPlayed)
                    .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                GameSortMode.PlayTime => filteredGames
                    .OrderByDescending(game => game.PlayTime)
                    .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                _ => filteredGames
                    .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        }

        private static IReadOnlyList<string> BuildInitialWarmupImagePaths(IReadOnlyList<Game> games, int columns)
        {
            int safeColumns = Math.Max(1, columns);
            int targetCount = Math.Min(InitialWarmupImageLimit, safeColumns * InitialWarmupRows);
            var imagePaths = new List<string>(targetCount);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < games.Count && imagePaths.Count < targetCount; index++)
            {
                string imagePath = games[index].ImageUrl;
                if (!string.IsNullOrWhiteSpace(imagePath) && seenPaths.Add(imagePath))
                {
                    imagePaths.Add(imagePath);
                }
            }

            return imagePaths;
        }
    }
}
