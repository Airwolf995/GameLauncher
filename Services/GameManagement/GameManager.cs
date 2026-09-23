using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using GameLauncher.Models;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Services.GameManagement
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
        private readonly MetadataService _metadataService = new();
        private readonly SteamMetadataCacheService _steamMetadataCache = new();
        private readonly GameLibraryLoader _libraryLoader = new();

        public GameConfig Config => _configService.Config;

        /// <summary>
        /// Der Konfigurationsdienst selbst. Wird fuer das Sichern und Einspielen
        /// der Konfiguration benoetigt, das Serialisierung, Dateipfad und das
        /// Anhalten weiterer Schreibvorgaenge zugleich braucht.
        /// </summary>
        internal ConfigService ConfigService => _configService;

        /// <summary>
        /// Plattformen, deren Scan im letzten Durchlauf fehlgeschlagen ist oder das
        /// Zeitlimit überschritten hat. Die Oberfläche weist damit auf eine
        /// unvollständige Bibliothek hin, statt sie stillschweigend zu verkürzen.
        /// </summary>
        public IReadOnlyList<string> LastScanFailures { get; private set; } = [];

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

        internal void UpdateConfig(Action<GameConfig> update)
        {
            _configService.UpdateConfig(update);
        }

        internal T ReadConfig<T>(Func<GameConfig, T> read)
        {
            return _configService.ReadConfig(read);
        }

        internal List<string> GetIgnoredProcessesSnapshot()
        {
            return _configService.ReadConfig(config =>
                (config.IgnoredProcesses ?? new List<string>()).ToList());
        }

        public async Task<List<Game>> LoadAllGamesAsync(bool loadSteamMetadataInBackground = true, System.Threading.CancellationToken ct = default)
        {
            var config = _configService.ReadConfig(currentConfig => new GameConfig
            {
                SteamLibraryPaths = currentConfig.SteamLibraryPaths.ToList(),
                EpicLibraryPaths = currentConfig.EpicLibraryPaths.ToList(),
                XboxLibraryPaths = currentConfig.XboxLibraryPaths.ToList(),
                ManualGames = currentConfig.ManualGames
                    .Select(CreateManualRuntimeGame)
                    .ToList()
            });
            var scanResult = await _libraryLoader.LoadAsync(config, ct);
            var games = scanResult.Games;
            LastScanFailures = scanResult.FailedPlatforms;

            ApplyStoredState(games);

            var currentLanguage = Services.Localization.LocalizationService.Instance.CurrentLanguage;
            int cachedMetadataCount = 0;
            foreach (var game in games)
            {
                if (_steamMetadataCache.ApplyCachedMetadata(game, currentLanguage))
                {
                    cachedMetadataCount++;
                }
            }

            if (cachedMetadataCount > 0)
            {
                Logger.Log($"Steam-Metadaten aus lokalem Cache übernommen: {cachedMetadataCount} Spiel(e).");
            }

            // Steam-Metadata throttled laden (max. 3 gleichzeitige Requests)
            var gamesNeedingMetadata = games
                .Where(g => g.Platform == "Steam" && _steamMetadataCache.NeedsRefresh(g, currentLanguage))
                .ToList();

            if (loadSteamMetadataInBackground && gamesNeedingMetadata.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshSteamMetadataBatchAsync(gamesNeedingMetadata, currentLanguage, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Background metadata fetch failed", ex);
                    }
                });
            }
                 
            return games;
        }

        public async Task<List<Game>> LoadDeferredStartupGamesAsync(System.Threading.CancellationToken ct = default)
        {
            var scanResult = await _libraryLoader.LoadDeferredAsync(ct);
            if (scanResult.FailedPlatforms.Count > 0)
            {
                LastScanFailures = LastScanFailures.Concat(scanResult.FailedPlatforms).Distinct().ToList();
            }

            ApplyStoredState(scanResult.Games);
            return scanResult.Games;
        }

        public async Task RefreshSteamMetadataAsync(IEnumerable<Game> games, System.Threading.CancellationToken ct = default)
        {
            var currentLanguage = Services.Localization.LocalizationService.Instance.CurrentLanguage;
            var steamGames = games
                .Where(game => game.Platform == Constants.Platforms.Steam &&
                               game.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (steamGames.Count == 0)
            {
                return;
            }

            int cachedMetadataCount = 0;
            foreach (var game in steamGames)
            {
                if (_steamMetadataCache.ApplyCachedMetadata(game, currentLanguage))
                {
                    cachedMetadataCount++;
                }
            }

            var steamGamesNeedingRefresh = steamGames
                .Where(game => _steamMetadataCache.NeedsRefresh(game, currentLanguage))
                .ToList();

            Logger.Log(
                $"Steam-Metadatenaktualisierung geplant: Gesamt={steamGames.Count}, Cache-Treffer={cachedMetadataCount}, Nachladen={steamGamesNeedingRefresh.Count}.");

            if (steamGamesNeedingRefresh.Count == 0)
            {
                return;
            }

            await RefreshSteamMetadataBatchAsync(steamGamesNeedingRefresh, currentLanguage, ct);
        }

        private async Task RefreshSteamMetadataBatchAsync(
            IReadOnlyCollection<Game> games,
            Services.Localization.AppLanguage language,
            System.Threading.CancellationToken cancellationToken)
        {
            using var semaphore = new System.Threading.SemaphoreSlim(3);
            var tasks = games.Select(async game =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (await _metadataService.FetchSteamMetadataAsync(game, cancellationToken))
                    {
                        _steamMetadataCache.Update(game, language);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Steam-Metadaten konnten für {game.Name} nicht geladen werden", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }


        public void LaunchGame(Game game, bool notifyUI = true)
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

                if (notifyUI)
                {
                    _stateService.RaiseGamesUpdated();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching game {game.Name}", ex);
                throw;
            }
        }

        /// <summary>
        /// Unterscheidet eine fehlende Datei von anderen Startfehlern. Process.Start
        /// meldet beides als Win32Exception, etwa auch eine abgelehnte
        /// UAC-Abfrage (1223); nur die Fehlercodes 2 und 3 bedeuten, dass Datei
        /// oder Ordner fehlen.
        /// </summary>
        internal static bool IsMissingFileError(Exception ex) =>
            ex is System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 };

        public Game AddManualGame(string name, string path, string args = "", string customImage = "", bool notifyUI = true)
        {
            string id = $"manual_{DateTime.Now.Ticks}";
            var game = new Game
            {
                Id = id,
                Source = "Manuell",
                IsManual = true
            };
            ApplyManualGameInput(game, name, path, args);

            if (!string.IsNullOrEmpty(customImage))
            {
                game.ImageUrl = customImage;
            }
            else if (game.LaunchType == "exe")
            {
                game.ImageUrl = IconExtractor.GetIconFromExe(game.Path, id);
            }

            _configService.UpdateConfig(config => config.ManualGames.Add(game));
            Logger.Log($"Added manual game: {name} ({game.Platform})");
            _configService.SaveConfig();
            
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }

            return CreateManualRuntimeGame(game);
        }

        /// <summary>
        /// Übernimmt Name, Pfad und Argumente eines manuell hinzugefügten Spiels.
        /// Die ID bleibt gleich, damit Spielzeit, Favorit, Tags und Bild erhalten
        /// bleiben. Die Änderung wird immer gemeldet: die Spielzeiterfassung
        /// sucht sonst weiter nach dem alten Pfad.
        /// </summary>
        public void UpdateManualGame(Game game, string name, string path, string args)
        {
            bool wasUpdated = false;
            _configService.UpdateConfig(config =>
            {
                var stored = config.ManualGames.FirstOrDefault(candidate => candidate.Id == game.Id);
                if (stored == null)
                {
                    return;
                }

                ApplyManualGameInput(stored, name, path, args);
                wasUpdated = true;
            });

            if (!wasUpdated)
            {
                return;
            }

            ApplyManualGameInput(game, name, path, args);
            _configService.SaveConfig();
            Logger.Log($"Updated manual game: {game.Name} ({game.Platform})");
            _stateService.RaiseGamesUpdated();
        }

        /// <summary>
        /// Leitet Plattform, Starttyp und Installationsordner aus dem Pfad ab.
        /// Hinzufügen und Bearbeiten nutzen dieselbe Regel, damit ein
        /// bearbeitetes Spiel genauso eingestuft wird wie ein neu angelegtes.
        /// </summary>
        internal static void ApplyManualGameInput(Game game, string name, string path, string args)
        {
            string platform = "Manuell";
            string launchType = "exe";

            if (path.Contains("://") || path.StartsWith("com.epicgames.launcher"))
            {
                launchType = "uri";
                if (path.Contains("battlenet")) platform = Constants.Platforms.BattleNet;
                if (path.Contains("epicgames")) platform = Constants.Platforms.Epic;
            }
            else
            {
                // Normalize Path (remove double backslashes, fix separators)
                try { path = Path.GetFullPath(path); } catch { /* Keep original if invalid */ }
            }

            game.Name = name;
            game.Path = path;
            game.Args = args;
            game.Platform = platform;
            game.LaunchType = launchType;
            game.InstallDirectory = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(path)) ?? "";
        }

        private static Game CreateManualRuntimeGame(Game source)
        {
            return new Game
            {
                Id = source.Id,
                Name = source.Name,
                Platform = source.Platform,
                Path = source.Path,
                Args = source.Args,
                InstallDirectory = source.InstallDirectory,
                ExecutableName = source.ExecutableName,
                Source = source.Source,
                LaunchType = source.LaunchType,
                IsManual = source.IsManual,
                ImageUrl = source.ImageUrl,
                Description = source.Description,
                Publisher = source.Publisher,
                Developer = source.Developer,
                ReleaseDate = source.ReleaseDate,
                Genres = source.Genres.ToList()
            };
        }

        public void RemoveManualGame(Game game, bool notifyUI = true)
        {
            bool wasRemoved = false;
            _configService.UpdateConfig(config =>
            {
                var toRemove = config.ManualGames.FirstOrDefault(candidate => candidate.Id == game.Id);
                if (toRemove == null)
                {
                    return;
                }

                config.ManualGames.Remove(toRemove);
                config.Favorites.Remove(game.Id);
                config.LastPlayed.Remove(game.Id);
                config.PlayTime.Remove(game.Id);
                config.HiddenGames.Remove(game.Id);
                config.GameTags.Remove(game.Id);
                config.ImageOverrides.Remove(game.Id);
                wasRemoved = true;
            });

            if (!wasRemoved)
            {
                return;
            }

            _imageService.CleanupImageIfUnused(game.Id, game.ImageUrl);
            _configService.SaveConfig();
            Logger.Log($"Removed manual game: {game.Name}");

            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }
        }

        // --- Delegated to GameStateService ---

        public void ToggleFavorite(Game game, bool notifyUI = true)
        {
            _stateService.ToggleFavorite(game);
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }
        }

        public void SetManualGameImage(Game game, string imagePath, bool notifyUI = true)
        {
            _imageService.SetManualGameImage(game, imagePath);
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }
        }

        /// <summary>
        /// Übernimmt ein über die Cover-Suche heruntergeladenes Bild. Meldet die
        /// Änderung nicht selbst; der Aufrufer bearbeitet das Spiel anschließend
        /// mit UpdateManualGame, das die Bibliothek aktualisiert.
        /// </summary>
        public void SetDownloadedGameImage(Game game, string downloadedImagePath) =>
            _imageService.SetDownloadedGameImage(game, downloadedImagePath);

        public void HideGame(Game game, bool notifyUI = true)
        {
            _stateService.HideGame(game);
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }
        }

        public void UnhideGame(Game game, bool notifyUI = true)
        {
            _stateService.UnhideGame(game);
            if (notifyUI)
            {
                _stateService.RaiseGamesUpdated();
            }
        }

        public void SetTheme(string themeName) => _stateService.SetTheme(themeName);

        public void UpdateLastPlayed(string gameId, DateTime lastPlayed) => _stateService.UpdateLastPlayed(gameId, lastPlayed);

        public void UpdatePlaySessions(IEnumerable<PlaySessionUpdate> updates, bool persistConfig = true) =>
            _stateService.UpdatePlaySessions(updates, persistConfig);

        public void NotifyGamesUpdated() => _stateService.RaiseGamesUpdated();

        private void ApplyStoredState(IEnumerable<Game> games)
        {
            _configService.ReadConfig(config =>
            {
                foreach (var game in games)
                {
                    // Gespeicherte Zustände bleiben die zentrale Quelle der Wahrheit.
                    game.PlayTime = 0;
                    game.LastPlayed = null;
                    game.IsFavorite = false;
                    game.IsHidden = false;

                    game.IsHidden = config.HiddenGames.Contains(game.Id);
                    game.IsFavorite = config.Favorites.Contains(game.Id);

                    if (config.LastPlayed.TryGetValue(game.Id, out DateTime lastPlayed))
                    {
                        game.LastPlayed = lastPlayed;
                    }

                    if (config.PlayTime.TryGetValue(game.Id, out var playTimeEntry))
                    {
                        game.PlayTime = playTimeEntry?.Seconds ?? 0;
                    }

                    if (config.ImageOverrides != null &&
                        config.ImageOverrides.TryGetValue(game.Id, out var customImage) &&
                        !string.IsNullOrWhiteSpace(customImage) &&
                        File.Exists(customImage))
                    {
                        game.ImageUrl = customImage;
                    }

                    if (config.GameTags != null &&
                        config.GameTags.TryGetValue(game.Id, out var tags) &&
                        tags != null)
                    {
                        game.Tags = new List<string>(tags);
                    }
                }
                return true;
            });
        }

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
            return _stateService.GetAllUsedTags();
        }
        
        #endregion

        public void Dispose()
        {
            _steamMetadataCache?.Dispose();
            _configService?.Dispose();
        }
    }
}
