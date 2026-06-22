using System;
using System.Collections.Generic;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    /// <summary>
    /// Handles game state mutations: favorites, hidden, tags, play time.
    /// Extracted from GameManager to follow Single Responsibility Principle.
    /// </summary>
    public class GameStateService
    {
        private readonly ConfigService _configService;

        /// <summary>
        /// Fired when the games collection or game state has changed.
        /// </summary>
        public event EventHandler? GamesUpdated;

        public GameStateService(ConfigService configService)
        {
            _configService = configService;
        }

        private GameConfig Config => _configService.Config;

        /// <summary>
        /// Toggles the favorite status of a game.
        /// </summary>
        public void ToggleFavorite(Game game)
        {
            if (Config.Favorites.Contains(game.Id))
            {
                Config.Favorites.Remove(game.Id);
                game.IsFavorite = false;
                Logger.Log($"Removed from favorites: {game.Name}");
            }
            else
            {
                Config.Favorites.Add(game.Id);
                game.IsFavorite = true;
                Logger.Log($"Added to favorites: {game.Name}");
            }
            _configService.SaveConfig();
        }

        /// <summary>
        /// Hides a game from the library view.
        /// </summary>
        public void HideGame(Game game)
        {
            if (!Config.HiddenGames.Contains(game.Id))
            {
                Config.HiddenGames.Add(game.Id);
                game.IsHidden = true;
                _configService.SaveConfig();
                Logger.Log($"Hidden game: {game.Name}");
            }
        }

        /// <summary>
        /// Shows a previously hidden game again.
        /// </summary>
        public void UnhideGame(Game game)
        {
            Config.HiddenGames.Remove(game.Id);
            game.IsHidden = false;
            _configService.SaveConfig();
            Logger.Log($"Unhidden game: {game.Name}");
        }

        /// <summary>
        /// Updates play time and last played for a single game.
        /// </summary>
        public void UpdatePlayTime(string gameId, int totalPlayTimeSeconds, string gameName = "")
        {
            Config.PlayTime[gameId] = CreatePlayTimeEntry(gameName, totalPlayTimeSeconds, gameId);
        }

        /// <summary>
        /// Updates last played timestamp for a single game.
        /// </summary>
        public void UpdateLastPlayed(string gameId, DateTime lastPlayed)
        {
            Config.LastPlayed[gameId] = lastPlayed;
        }

        /// <summary>
        /// Batch update for play sessions (called from PlayTimeService).
        /// </summary>
        public void UpdatePlaySessions(IEnumerable<PlaySessionUpdate> updates)
        {
            foreach (var update in updates)
            {
                Config.PlayTime[update.GameId] = CreatePlayTimeEntry(update.GameName, update.PlayTimeSeconds, update.GameId);
                Config.LastPlayed[update.GameId] = update.LastPlayed;
            }
            _configService.SaveConfig();
        }

        private PlayTimeEntry CreatePlayTimeEntry(string? gameName, int totalPlayTimeSeconds, string gameId)
        {
            var existingName = Config.PlayTime.TryGetValue(gameId, out var existingEntry)
                ? existingEntry?.Name
                : null;

            return new PlayTimeEntry
            {
                Name = string.IsNullOrWhiteSpace(gameName)
                    ? (string.IsNullOrWhiteSpace(existingName) ? gameId : existingName)
                    : gameName,
                Seconds = totalPlayTimeSeconds
            };
        }

        /// <summary>
        /// Sets the UI theme.
        /// </summary>
        public void SetTheme(string themeName)
        {
            Config.Theme = themeName;
            _configService.SaveConfig();
        }

        /// <summary>
        /// Adds a tag to a game.
        /// </summary>
        public void AddTag(Game game, string tag)
        {
            if (game.Tags.Contains(tag)) return;

            game.Tags.Add(tag);

            if (!Config.GameTags.ContainsKey(game.Id))
            {
                Config.GameTags[game.Id] = new List<string>();
            }
            Config.GameTags[game.Id].Add(tag);
            _configService.SaveConfig();
            Logger.Log($"Added tag '{tag}' to game '{game.Name}'.");
        }

        /// <summary>
        /// Removes a tag from a game.
        /// </summary>
        public void RemoveTag(Game game, string tag)
        {
            game.Tags.Remove(tag);

            if (Config.GameTags.ContainsKey(game.Id))
            {
                Config.GameTags[game.Id].Remove(tag);
                if (Config.GameTags[game.Id].Count == 0)
                {
                    Config.GameTags.Remove(game.Id);
                }
            }
            _configService.SaveConfig();
            Logger.Log($"Removed tag '{tag}' from game '{game.Name}'.");
        }

        /// <summary>
        /// Gets all unique tags used across all games.
        /// </summary>
        public IEnumerable<string> GetAllUsedTags()
        {
            return Config.GameTags.Values
                .SelectMany(tags => tags)
                .Distinct()
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// Raises the GamesUpdated event.
        /// </summary>
        public void RaiseGamesUpdated()
        {
            GamesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }
}
