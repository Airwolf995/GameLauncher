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

        /// <summary>
        /// Toggles the favorite status of a game.
        /// </summary>
        public void ToggleFavorite(Game game)
        {
            bool wasFavorite = false;
            _configService.UpdateConfig(config =>
            {
                wasFavorite = config.Favorites.Contains(game.Id);
                if (wasFavorite)
                {
                    config.Favorites.Remove(game.Id);
                }
                else
                {
                    config.Favorites.Add(game.Id);
                }
            });

            game.IsFavorite = !wasFavorite;
            Logger.Log(wasFavorite
                ? $"Removed from favorites: {game.Name}"
                : $"Added to favorites: {game.Name}");
            _configService.SaveConfig();
        }

        /// <summary>
        /// Hides a game from the library view.
        /// </summary>
        public void HideGame(Game game)
        {
            bool wasAdded = false;
            _configService.UpdateConfig(config =>
            {
                wasAdded = config.HiddenGames.Add(game.Id);
            });

            if (wasAdded)
            {
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
            _configService.UpdateConfig(config => config.HiddenGames.Remove(game.Id));
            game.IsHidden = false;
            _configService.SaveConfig();
            Logger.Log($"Unhidden game: {game.Name}");
        }

        /// <summary>
        /// Updates last played timestamp for a single game.
        /// </summary>
        public void UpdateLastPlayed(string gameId, DateTime lastPlayed)
        {
            _configService.UpdateConfig(config => config.LastPlayed[gameId] = lastPlayed);
        }

        /// <summary>
        /// Batch update for play sessions (called from PlayTimeService).
        /// </summary>
        public void UpdatePlaySessions(IEnumerable<PlaySessionUpdate> updates, bool persistConfig = true)
        {
            var updatesSnapshot = updates.ToList();
            _configService.UpdateConfig(config =>
            {
                foreach (var update in updatesSnapshot)
                {
                    config.PlayTime[update.GameId] = CreatePlayTimeEntry(config, update.GameName, update.PlayTimeSeconds, update.GameId);
                    config.LastPlayed[update.GameId] = update.LastPlayed;
                }
            });
            if (persistConfig)
            {
                _configService.SaveConfig();
            }
        }

        private static PlayTimeEntry CreatePlayTimeEntry(GameConfig config, string? gameName, int totalPlayTimeSeconds, string gameId)
        {
            var existingName = config.PlayTime.TryGetValue(gameId, out var existingEntry)
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
            _configService.UpdateConfig(config => config.Theme = themeName);
            _configService.SaveConfig();
        }

        /// <summary>
        /// Adds a tag to a game.
        /// </summary>
        public void AddTag(Game game, string tag)
        {
            if (game.Tags.Contains(tag)) return;

            game.Tags.Add(tag);
            _configService.UpdateConfig(config =>
            {
                if (!config.GameTags.TryGetValue(game.Id, out var tags))
                {
                    tags = new List<string>();
                    config.GameTags[game.Id] = tags;
                }

                tags.Add(tag);
            });
            _configService.SaveConfig();
            Logger.Log($"Added tag '{tag}' to game '{game.Name}'.");
        }

        /// <summary>
        /// Removes a tag from a game.
        /// </summary>
        public void RemoveTag(Game game, string tag)
        {
            game.Tags.Remove(tag);
            _configService.UpdateConfig(config =>
            {
                if (config.GameTags.TryGetValue(game.Id, out var tags))
                {
                    tags.Remove(tag);
                    if (tags.Count == 0)
                    {
                        config.GameTags.Remove(game.Id);
                    }
                }
            });
            _configService.SaveConfig();
            Logger.Log($"Removed tag '{tag}' from game '{game.Name}'.");
        }

        /// <summary>
        /// Gets all unique tags used across all games.
        /// </summary>
        public List<string> GetAllUsedTags()
        {
            return _configService.ReadConfig(config => config.GameTags.Values
                .SelectMany(tags => tags)
                .Distinct()
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
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
