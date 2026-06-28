using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    public class PlayTimeService : IDisposable
    {
        private const int TickIntervalSeconds = 15;
        private const int SummaryLogEveryNTicks = 8; // 8 * 15s = 2 minutes
        private readonly GameManager _gameManager;
        private readonly IEnumerable<Game> _games;
        private readonly System.Timers.Timer _timer;
        private readonly PlayTimeMatchIndex _matchIndex = new();
        private readonly ActiveGameTracker _activeGameTracker = new();
        private int _isTickRunning;
        private int _tickCounter;
        private int _lastIndexedGameCount;
        private volatile bool _indexDirty;
        private HashSet<string> _cachedIgnoredProcesses = new(StringComparer.OrdinalIgnoreCase);
        
        private static readonly HashSet<string> WindowsSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", 
            "svchost", "fontdrvhost", "dwm", "spoolsv", "SearchIndexer", "explorer", 
            "conhost", "dllhost", "taskhostw", "RuntimeBroker", "SearchHost", 
            "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "ctfmon",
            "audiodg", "SgrmBroker", "smartscreen", "SecurityHealthService", "dasHost",
            "wlanext", "msedge", "chrome", "firefox", "teams", "discord"
        };

#if DEBUG
        private int _debugLogThrottle;
#endif

        public event EventHandler<Game>? PlayTimeUpdated;
        public Game? ActiveGame { get; private set; }
        public DateTime? SessionStartTime { get; private set; }

        public PlayTimeService(GameManager gameManager, IEnumerable<Game> games)
        {
            _gameManager = gameManager;
            _games = games;
            
            // Index bei Spieleänderungen automatisch als dirty markieren
            _gameManager.GamesUpdated += OnGamesUpdated;
            
            // Check every 15 seconds - PlayTime is in seconds for high precision
            _timer = new System.Timers.Timer(TickIntervalSeconds * 1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            var gamesSnapshot = CaptureGamesSnapshotOnUiThread();
            _matchIndex.Rebuild(gamesSnapshot);
            _lastIndexedGameCount = gamesSnapshot.Count;
            _cachedIgnoredProcesses = new HashSet<string>(
                _gameManager.Config.IgnoredProcesses ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            _indexDirty = false;
            _timer.Start();
            Logger.Log("PlayTimeService started (15s interval, tracking in seconds).");
        }

        public void Stop()
        {
            _timer.Stop();
            Logger.Log("PlayTimeService stopped.");
        }

        public void Dispose()
        {
            Stop();
            _gameManager.GamesUpdated -= OnGamesUpdated;
            _timer.Dispose();
        }

        private void OnGamesUpdated(object? sender, EventArgs e)
        {
            _indexDirty = true;
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (Interlocked.Exchange(ref _isTickRunning, 1) == 1)
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                int indexedGameCount = Volatile.Read(ref _lastIndexedGameCount);

                // Rebuild nur wenn sich die Spieleliste geändert hat (event-basiert)
                if (_indexDirty)
                {
                    var gamesSnapshot = CaptureGamesSnapshotOnUiThread();
                    _matchIndex.Rebuild(gamesSnapshot);
                    indexedGameCount = gamesSnapshot.Count;
                    Volatile.Write(ref _lastIndexedGameCount, indexedGameCount);
                    _cachedIgnoredProcesses = new HashSet<string>(
                        _gameManager.Config.IgnoredProcesses ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    _indexDirty = false;
                }

                var processes = Process.GetProcesses();
                var runningGameIds = new HashSet<string>(StringComparer.Ordinal);
                var runningGameStartedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);

#if DEBUG
                if ((_debugLogThrottle++ % 20) == 0)
                {
                    Logger.Log($"[DEBUG] PlayTimeService index scan: {indexedGameCount} games, {processes.Length} processes.");
                }
#endif

                foreach (var process in processes)
                {
                    try
                    {
                        var processName = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(processName))
                        {
                            continue;
                        }

                        // 1. Ignorierte und Windows-Systemprozesse direkt überspringen
                        if (WindowsSystemProcesses.Contains(processName) || 
                            _cachedIgnoredProcesses.Contains(processName) || 
                            _cachedIgnoredProcesses.Contains(processName + ".exe"))
                        {
                            continue;
                        }

                        // 2. Schnellprüfung über Name (ohne teures MainModule)
                        if (_matchIndex.TryMatchProcessByName(processName, out var matchedGameId))
                        {
                            AddRunningGameMatch(runningGameIds, runningGameStartedAt, matchedGameId, TryGetProcessStartTime(process, now));
                            continue;
                        }

                        // 3. Fallback: Pfadprüfung für Verzeichnis-basierte Treffer (Steam, Epic)
                        string? processPathRaw = null;
                        try
                        {
                            processPathRaw = process.MainModule?.FileName;
                        }
                        catch
                        {
                            // Zugriff verweigert oder Prozess beendet
                        }

                        if (!string.IsNullOrWhiteSpace(processPathRaw))
                        {
                            var processPath = PlayTimeMatchIndex.NormalizePath(processPathRaw);
                            if (!string.IsNullOrWhiteSpace(processPath) &&
                                _matchIndex.TryMatchProcess(processName, processPath, out matchedGameId))
                            {
                                AddRunningGameMatch(runningGameIds, runningGameStartedAt, matchedGameId, TryGetProcessStartTime(process, now));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Fehler abfangen
                    }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }

                var activeGameId = _activeGameTracker.UpdateAndSelectActiveGameId(runningGameIds, now);
                DateTime? activeGameStartedAt = activeGameId != null && runningGameStartedAt.TryGetValue(activeGameId, out var startedAt)
                    ? startedAt
                    : null;
                var updatedGameNames = new List<string>();
                var sessionUpdates = new List<PlaySessionUpdate>();
                if (runningGameIds.Count > 0 || ActiveGame != null || SessionStartTime != null)
                {
                    sessionUpdates = ApplyPlayTimeUpdatesOnUiThread(now, activeGameId, activeGameStartedAt, runningGameIds, updatedGameNames);
                }

                if (sessionUpdates.Count > 0)
                {
                    _gameManager.UpdatePlaySessions(sessionUpdates);
                }

                var tickNumber = Interlocked.Increment(ref _tickCounter);
                if (updatedGameNames.Count > 0 && (tickNumber % SummaryLogEveryNTicks) == 0)
                {
#if DEBUG
                    Logger.Log($"[DEBUG] PlayTime tick summary: +{TickIntervalSeconds}s for {updatedGameNames.Count} game(s): {string.Join(", ", updatedGameNames)}.");
#else
                    Logger.Log($"PlayTime tick summary: +{TickIntervalSeconds}s for {updatedGameNames.Count} game(s).");
#endif
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error in PlayTimeService timer", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isTickRunning, 0);
            }
        }

        private List<Game> CaptureGamesSnapshotOnUiThread()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return _games.ToList();
            }

            return dispatcher.Invoke(() => _games.ToList());
        }

        private static void AddRunningGameMatch(
            ISet<string> runningGameIds,
            IDictionary<string, DateTime> runningGameStartedAt,
            string gameId,
            DateTime startedAt)
        {
            runningGameIds.Add(gameId);
            if (!runningGameStartedAt.TryGetValue(gameId, out var existingStartedAt) || startedAt < existingStartedAt)
            {
                runningGameStartedAt[gameId] = startedAt;
            }
        }

        private static DateTime TryGetProcessStartTime(Process process, DateTime fallback)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return fallback;
            }
        }

        private List<PlaySessionUpdate> ApplyPlayTimeUpdatesOnUiThread(
            DateTime now,
            string? activeGameId,
            DateTime? activeGameStartedAt,
            HashSet<string> runningGameIds,
            List<string> updatedGameNames)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return ApplyPlayTimeUpdates(now, activeGameId, activeGameStartedAt, runningGameIds, updatedGameNames);
            }

            return dispatcher.Invoke(() => ApplyPlayTimeUpdates(now, activeGameId, activeGameStartedAt, runningGameIds, updatedGameNames));
        }

        private List<PlaySessionUpdate> ApplyPlayTimeUpdates(
            DateTime now,
            string? activeGameId,
            DateTime? activeGameStartedAt,
            HashSet<string> runningGameIds,
            List<string> updatedGameNames)
        {
            var sessionUpdates = new List<PlaySessionUpdate>();

            if (string.IsNullOrWhiteSpace(activeGameId))
            {
                ActiveGame = null;
                SessionStartTime = null;
            }
            else
            {
                var activeGame = _matchIndex.GetGameById(activeGameId);
                var sessionStartTime = activeGameStartedAt ?? now;
                if (activeGame != null && ActiveGame?.Id != activeGame.Id)
                {
                    ActiveGame = activeGame;
                    SessionStartTime = sessionStartTime;
                    Logger.Log($"New active game detected for Overlay: {activeGame.Name}");
                }
                else if (activeGame != null &&
                         (!SessionStartTime.HasValue || sessionStartTime < SessionStartTime.Value))
                {
                    SessionStartTime = sessionStartTime;
                }
            }

            // Increment playtime for all identified running games
            foreach (var gameId in runningGameIds)
            {
                var game = _matchIndex.GetGameById(gameId);
                if (game == null)
                {
                    continue;
                }

                game.PlayTime += TickIntervalSeconds;
                game.LastPlayed = now;
                PlayTimeUpdated?.Invoke(this, game);
                updatedGameNames.Add(game.Name);
                sessionUpdates.Add(new PlaySessionUpdate(gameId, game.Name, game.PlayTime, now));
            }

            return sessionUpdates;
        }
    }
}
