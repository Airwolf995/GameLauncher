using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using GameLauncher.Services;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Models
{
    /// <summary>
    /// Facade for game management. Delegates to specialized services:
    /// - ConfigService: Configuration persistence
    /// - GameStateService: Favorites, hidden, tags, play time
    /// - GameImageService: Cover image management
    /// </summary>
    public class GameManager : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly GameStateService _stateService;
        private readonly GameImageService _imageService;
        private readonly Services.MetadataService _metadataService = new();

        public GameConfig Config => _configService.Config;
        
        public event EventHandler? GamesUpdated
        {
            add => _stateService.GamesUpdated += value;
            remove => _stateService.GamesUpdated -= value;
        }

        public GameManager() : this(null)
        {
        }

        internal GameManager(string? configPathOverride)
        {
            _configService = configPathOverride != null 
                ? new ConfigService(configPathOverride) 
                : new ConfigService();
            _stateService = new GameStateService(_configService);
            _imageService = new GameImageService(_configService);
        }

        public GameConfig GetConfig()
        {
            return _configService.Config;
        }

        public void SaveConfig()
        {
            _configService.SaveConfig();
        }

        public void SaveConfigImmediate(GameConfig config)
        {
            _configService.SaveConfigImmediate(config);
        }

        public async Task<List<Game>> LoadAllGamesAsync(System.Threading.CancellationToken ct = default)
        {
            var games = new List<Game>();
            var config = _configService.Config;

            // 1. Scan Platforms in Parallel using dedicated scanners
            Logger.Log("Starting scanning games parallel...");

            var steamScanner = new SteamScanner(config.SteamLibraryPaths);
            var gogScanner = new GogScanner();
            var epicScanner = new EpicScanner(config.EpicLibraryPaths);
            var ubiScanner = new UbisoftScanner();
            var eaScanner = new EaScanner();
            var xboxScanner = new XboxScanner(config.XboxLibraryPaths);

            var steamTask = steamScanner.ScanAsync(ct);
            var gogTask = gogScanner.ScanAsync(ct);
            var epicTask = epicScanner.ScanAsync(ct);
            var ubiTask = ubiScanner.ScanAsync(ct);
            var eaTask = eaScanner.ScanAsync(ct);
            var xboxTask = xboxScanner.ScanAsync(ct);

            await Task.WhenAll(steamTask, gogTask, epicTask, ubiTask, eaTask, xboxTask);

            Logger.Log($"Parallel scan finished. Steam: {steamTask.Result.Count}, GOG: {gogTask.Result.Count}, Epic: {epicTask.Result.Count}, Ubi: {ubiTask.Result.Count}, EA: {eaTask.Result.Count}, Xbox: {xboxTask.Result.Count}");

            games.AddRange(steamTask.Result);
            games.AddRange(gogTask.Result);
            games.AddRange(epicTask.Result);
            games.AddRange(ubiTask.Result);
            games.AddRange(eaTask.Result);
            games.AddRange(xboxTask.Result);

            // 2. Manual Games
            if (config.ManualGames != null)
            {
                foreach (var mGame in config.ManualGames)
                {
                    mGame.IsManual = true; // Ensure logic consistency
                    
                    // Backfill icon if missing and it is an exe
                    if (string.IsNullOrEmpty(mGame.ImageUrl) && mGame.LaunchType == "exe" && File.Exists(mGame.Path))
                    {
                         mGame.ImageUrl = IconExtractor.GetIconFromExe(mGame.Path, mGame.Id);
                    }

                    // Populate InstallDirectory for manual games if not set
                    if (string.IsNullOrEmpty(mGame.InstallDirectory) && !string.IsNullOrEmpty(mGame.Path) && mGame.LaunchType == "exe")
                    {
                        mGame.InstallDirectory = Path.GetDirectoryName(mGame.Path) ?? "";
                    }
                }
                games.AddRange(config.ManualGames);
                Logger.Log($"Loaded {config.ManualGames.Count} manual games.");
            }

            // 3. Apply Favorites, Last Played, PlayTime & Hidden Status

            foreach (var game in games)
            {
                // Reset state to ensure global dictionaries are the source of truth
                game.PlayTime = 0;
                game.LastPlayed = null;
                game.IsFavorite = false;
                game.IsHidden = false;

                // Mark Hidden Status
                game.IsHidden = config.HiddenGames.Contains(game.Id);
                game.IsFavorite = config.Favorites.Contains(game.Id);
                
                if (config.LastPlayed.TryGetValue(game.Id, out DateTime lastPlayed))
                {
                    game.LastPlayed = lastPlayed;
                }

                if (config.PlayTime.TryGetValue(game.Id, out int playTime))
                {
                    game.PlayTime = playTime;
                }
                
                // Apply Image Override
                if (config.ImageOverrides != null && config.ImageOverrides.TryGetValue(game.Id, out var customImage) && !string.IsNullOrWhiteSpace(customImage))
                {
                    if (File.Exists(customImage))
                    {
                        game.ImageUrl = customImage;
                    }
                }
                
                // Apply Tags
                if (config.GameTags != null && config.GameTags.TryGetValue(game.Id, out var tags) && tags != null)
                {
                    game.Tags = new List<string>(tags);
                }
            }

            // Steam-Metadata throttled laden (max. 3 gleichzeitige Requests)
            var gamesNeedingMetadata = games
                .Where(g => g.Platform == "Steam" && string.IsNullOrEmpty(g.Description))
                .ToList();

            if (gamesNeedingMetadata.Count > 0)
            {
                var semaphore = new System.Threading.SemaphoreSlim(3);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var tasks = gamesNeedingMetadata.Select(async game =>
                        {
                            await semaphore.WaitAsync(ct);
                            try
                            {
                                await _metadataService.FetchSteamMetadataAsync(game, ct);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Metadata fetch failed for {game.Name}", ex);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                        await Task.WhenAll(tasks);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Background metadata fetch failed", ex);
                    }
                });
            }
                 
            return games;
        }

        public async Task RefreshSteamMetadataAsync(IEnumerable<Game> games, System.Threading.CancellationToken ct = default)
        {
            var steamGames = games
                .Where(game => game.Platform == Constants.Platforms.Steam && game.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (steamGames.Count == 0)
            {
                return;
            }

            using var semaphore = new System.Threading.SemaphoreSlim(3);
            var tasks = steamGames.Select(async game =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await _metadataService.FetchSteamMetadataAsync(game, ct);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Metadata refresh failed for {game.Name}", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }


        public void LaunchGame(Game game)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                string finalPath = Environment.ExpandEnvironmentVariables(game.Path ?? string.Empty);
                string finalArgs = Environment.ExpandEnvironmentVariables(game.Args ?? string.Empty);
                
                if (game.LaunchType == "uri")
                {
                    psi.FileName = finalPath;
                    psi.UseShellExecute = true;
                    Logger.Log($"Launching URI: {game.Path}");
                }
                else
                {
                    psi.FileName = finalPath;
                    psi.Arguments = finalArgs;
                    psi.WorkingDirectory = Path.GetDirectoryName(finalPath);
                    psi.UseShellExecute = true; // Often safer for games
                    Logger.Log($"Launching EXE: {finalPath} {finalArgs}");
                }

                Process.Start(psi);
                
                _stateService.RaiseGamesUpdated();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching game {game.Name}", ex);
                throw;
            }
        }

        public Game AddManualGame(string name, string path, string args = "", string customImage = "", bool notifyUI = true)
        {
            var config = _configService.Config;

            // Detect Platform/Type
            string id = $"manual_{DateTime.Now.Ticks}";
            string platform = "Manuell";
            string launchType = "exe";

            if (path.Contains("://") || path.StartsWith("com.epicgames.launcher"))
            {
                launchType = "uri";
                if (path.Contains("battlenet")) platform = "Battle.net";
                if (path.Contains("epicgames")) platform = "Epic Games";
            }
            else
            {
                // Normalize Path (remove double backslashes, fix separators)
                try { path = Path.GetFullPath(path); } catch { /* Keep original if invalid */ }
            }

            string imageUrl = "";
            if (!string.IsNullOrEmpty(customImage))
            {
                imageUrl = customImage;
            }
            else if (launchType == "exe")
            {
                imageUrl = IconExtractor.GetIconFromExe(path, id);
            }

            var game = new Game
            {
                Id = id,
                Name = name,
                Path = path,
                Args = args,
                Platform = platform,
                Source = "Manuell",
                LaunchType = launchType,
                IsManual = true,
                ImageUrl = imageUrl,
                InstallDirectory = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(path)) ?? ""
            };

            config.ManualGames.Add(game);
            Logger.Log($"Added manual game: {name} ({platform})");
            _configService.SaveConfig();
            
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }

            return game;
        }

        public void RemoveManualGame(Game game, bool notifyUI = true)
        {
            var config = _configService.Config;
            var toRemove = config.ManualGames.FirstOrDefault(g => g.Id == game.Id);
            if (toRemove != null)
            {
                config.ManualGames.Remove(toRemove);
                
                // Cleanup config
                if (config.Favorites.Contains(game.Id)) config.Favorites.Remove(game.Id);
                if (config.LastPlayed.ContainsKey(game.Id)) config.LastPlayed.Remove(game.Id);
                
                // Cleanup cached image
                if (!string.IsNullOrEmpty(game.ImageUrl) && File.Exists(game.ImageUrl))
                {
                    try 
                    {
                        // Check if any other manual game or override uses the same image URL
                        bool isShared = config.ManualGames.Any(g => g.Id != game.Id && string.Equals(g.ImageUrl, game.ImageUrl, StringComparison.OrdinalIgnoreCase)) ||
                                        (config.ImageOverrides != null && config.ImageOverrides.Any(kvp => kvp.Key != game.Id && string.Equals(kvp.Value, game.ImageUrl, StringComparison.OrdinalIgnoreCase)));

                        if (!isShared)
                        {
                            // Only delete if it is in our cache directory
                            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            string cacheDir = Path.Combine(documentsPath, "GameLauncher", "Cache");
                            if (Path.GetFullPath(game.ImageUrl).StartsWith(Path.GetFullPath(cacheDir), StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(game.ImageUrl);
                                Logger.Log($"Deleted cached image for: {game.Name}");
                            }
                        }
                        else
                        {
                            Logger.Log($"Skipped deleting image {game.ImageUrl} as it is still shared by another game.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to delete image: {game.ImageUrl}", ex);
                    }
                }
                
                _configService.SaveConfig();
                Logger.Log($"Removed manual game: {game.Name}");
                
                if (notifyUI)
                {
                    _stateService.RaiseGamesUpdated();
                }
            }
        }

        // --- Delegated to GameStateService ---

        public void ToggleFavorite(Game game)
        {
            _stateService.ToggleFavorite(game);
            _stateService.RaiseGamesUpdated();
        }

        public void SetManualGameImage(Game game, string imagePath)
        {
            _imageService.SetManualGameImage(game, imagePath);
            _stateService.RaiseGamesUpdated();
        }

        public void HideGame(Game game)
        {
            _stateService.HideGame(game);
            _stateService.RaiseGamesUpdated();
        }

        public void UnhideGame(Game game)
        {
            _stateService.UnhideGame(game);
            _stateService.RaiseGamesUpdated();
        }

        public void SetTheme(string themeName) => _stateService.SetTheme(themeName);

        public void UpdatePlayTime(string gameId, int seconds) => _stateService.UpdatePlayTime(gameId, seconds);

        public void UpdateLastPlayed(string gameId, DateTime lastPlayed) => _stateService.UpdateLastPlayed(gameId, lastPlayed);

        public void UpdatePlaySessions(IEnumerable<PlaySessionUpdate> updates) => _stateService.UpdatePlaySessions(updates);

        #region Tag Management
        
        public void AddTag(Game game, string tag)
        {
            _stateService.AddTag(game, tag);
            _stateService.RaiseGamesUpdated();
        }

        public void RemoveTag(Game game, string tag)
        {
            _stateService.RemoveTag(game, tag);
            _stateService.RaiseGamesUpdated();
        }

        public List<string> GetAllUsedTags()
        {
            return _stateService.GetAllUsedTags().ToList();
        }
        
        #endregion

        public void Dispose()
        {
            _configService?.Dispose();
        }
    }
}
