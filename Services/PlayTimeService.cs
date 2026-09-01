using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Services
{
    public class PlayTimeService : IDisposable
    {
        // Ein Spiel wird nur erfasst, wenn ein Durchlauf in seine Laufzeit fällt.
        // Ein kürzeres Intervall erfasst daher auch kurze Sitzungen zuverlässiger
        // und begrenzt zugleich die Ungenauigkeit am Sitzungsende.
        private const int TickIntervalSeconds = 10;

        // Im Leerlauf laeuft der Scan seltener: jeder Durchlauf legt mehrere hundert
        // Prozessobjekte an, und solange kein Spiel laeuft, ist daran nichts zu holen.
        // Sobald ein Spiel erkannt wurde, wird wieder im kurzen Takt gemessen, damit
        // die Spielzeiterfassung unveraendert genau bleibt.
        private const int IdleTickIntervalSeconds = 30;
        private const int SummaryLogEveryNTicks = 12; // 12 * 10s = 2 Minuten
        private const int PersistEveryNTicks = 6; // Spielzeit höchstens einmal pro Minute regulär schreiben
        private readonly GameManager _gameManager;
        private readonly IEnumerable<Game> _games;
        private readonly System.Timers.Timer _timer;
        private readonly Action? _tickBody;
        private readonly object _lifecycleSync = new();
        private readonly PlayTimeMatchIndex _matchIndex = new();
        private readonly ActiveGameTracker _activeGameTracker = new();
        private int _isTickRunning;
        private int _tickCounter;
        private int _lastIndexedGameCount;
        private volatile bool _indexDirty;
        private TaskCompletionSource<bool> _tickCompleted = CreateCompletedTickSource();
        private bool _isRunning;
        private bool _disposed;
        private HashSet<string> _cachedIgnoredProcesses = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Zuletzt erkannte Spiele. Dient dazu, nur die Wechsel zu protokollieren
        /// statt jeden Durchlauf, damit im Protokoll nachvollziehbar bleibt, ob und
        /// welchem Spiel ein laufender Prozess zugeordnet wurde.
        /// </summary>
        private HashSet<string> _previouslyRunningGameIds = new(StringComparer.Ordinal);

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

        public Game? ActiveGame { get; private set; }
        public DateTime? SessionStartTime { get; private set; }

        public PlayTimeService(GameManager gameManager, IEnumerable<Game> games)
            : this(gameManager, games, null)
        {
        }

        internal PlayTimeService(
            GameManager gameManager,
            IEnumerable<Game> games,
            Action? tickBody)
        {
            _gameManager = gameManager;
            _games = games;
            _tickBody = tickBody;
            
            // Index bei Spieleänderungen automatisch als dirty markieren
            _gameManager.GamesUpdated += OnGamesUpdated;
            
            // Check every 15 seconds - PlayTime is in seconds for high precision
            _timer = new System.Timers.Timer(TickIntervalSeconds * 1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            lock (_lifecycleSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var gamesSnapshot = CaptureGamesSnapshotOnUiThread();
                _matchIndex.Rebuild(gamesSnapshot);
                _lastIndexedGameCount = gamesSnapshot.Count;
                _cachedIgnoredProcesses = new HashSet<string>(
                    _gameManager.GetIgnoredProcessesSnapshot(),
                    StringComparer.OrdinalIgnoreCase);
                _indexDirty = false;
                _isRunning = true;
                _timer.Start();
                Logger.Log($"PlayTimeService started ({TickIntervalSeconds}s interval, tracking in seconds).");
            }
        }

        public void Stop()
        {
            lock (_lifecycleSync)
            {
                if (_disposed)
                {
                    return;
                }

                _isRunning = false;
                _timer.Stop();
                Logger.Log("PlayTimeService stopped.");
            }
        }

        public Task StopAsync()
        {
            lock (_lifecycleSync)
            {
                if (!_disposed)
                {
                    _isRunning = false;
                    _timer.Stop();
                }

                return _tickCompleted.Task;
            }
        }

        public void Dispose()
        {
            Task runningTick;
            lock (_lifecycleSync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _isRunning = false;
                _timer.Stop();
                _timer.Elapsed -= OnTimerElapsed;
                runningTick = _tickCompleted.Task;
            }

            runningTick.GetAwaiter().GetResult();
            _gameManager.GamesUpdated -= OnGamesUpdated;
            _timer.Dispose();
            Logger.Log("PlayTimeService stopped.");
        }

        private void OnGamesUpdated(object? sender, EventArgs e)
        {
            _indexDirty = true;
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            RunTick();
        }

        internal void RunTick()
        {
            TaskCompletionSource<bool> currentTickCompletion;
            lock (_lifecycleSync)
            {
                if (_disposed || !_isRunning || Interlocked.Exchange(ref _isTickRunning, 1) == 1)
                {
                    return;
                }

                currentTickCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _tickCompleted = currentTickCompletion;
            }

            try
            {
                if (_tickBody != null)
                {
                    _tickBody();
                    return;
                }

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
                        _gameManager.GetIgnoredProcessesSnapshot(),
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
                        // Der Pfad wird ohne Ausnahmebehandlung ermittelt; ein
                        // verweigerter Zugriff liefert schlicht keinen Pfad.
                        string? processPathRaw = ProcessPathReader.TryGetExecutablePath(process.Id);

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

                LogRunningGameChanges(runningGameIds);
                ApplyTickInterval(runningGameIds.Count > 0);

                var activeGameId = _activeGameTracker.UpdateAndSelectActiveGameId(runningGameIds, now);
                DateTime? activeGameStartedAt = activeGameId != null && runningGameStartedAt.TryGetValue(activeGameId, out var startedAt)
                    ? startedAt
                    : null;
                var updatedGameNames = new List<string>();
                var sessionUpdates = new List<PlaySessionUpdate>();
                bool hadTrackedSession = ActiveGame != null || SessionStartTime != null;
                if (runningGameIds.Count > 0 || ActiveGame != null || SessionStartTime != null)
                {
                    sessionUpdates = ApplyPlayTimeUpdatesOnUiThread(now, activeGameId, activeGameStartedAt, runningGameIds, updatedGameNames);
                }

                var tickNumber = Interlocked.Increment(ref _tickCounter);
                if (sessionUpdates.Count > 0)
                {
                    bool persistConfig = (tickNumber % PersistEveryNTicks) == 0;
                    _gameManager.UpdatePlaySessions(sessionUpdates, persistConfig);
                }
                else if (hadTrackedSession)
                {
                    _gameManager.SaveConfig();
                }

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
                lock (_lifecycleSync)
                {
                    currentTickCompletion.TrySetResult(true);
                    Interlocked.Exchange(ref _isTickRunning, 0);
                }
            }
        }

        /// <summary>
        /// Das Scan-Intervall abhaengig davon, ob gerade ein Spiel laeuft.
        /// </summary>
        internal static int GetTickIntervalSeconds(bool anyGameRunning) =>
            anyGameRunning ? TickIntervalSeconds : IdleTickIntervalSeconds;

        internal double CurrentTickIntervalMs => _timer.Interval;

        private void ApplyTickInterval(bool anyGameRunning)
        {
            double desiredIntervalMs = GetTickIntervalSeconds(anyGameRunning) * 1000d;

            lock (_lifecycleSync)
            {
                if (_disposed || Math.Abs(_timer.Interval - desiredIntervalMs) < 1d)
                {
                    return;
                }

                _timer.Interval = desiredIntervalMs;
                Logger.Log(
                    $"PlayTime-Scanintervall auf {desiredIntervalMs / 1000d:0}s gesetzt " +
                    $"({(anyGameRunning ? "Spiel laeuft" : "Leerlauf")}).");
            }
        }

        private static TaskCompletionSource<bool> CreateCompletedTickSource()
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(true);
            return source;
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

        /// <summary>
        /// Protokolliert Beginn und Ende der erkannten Spiele. Nur Wechsel werden
        /// gemeldet, damit das Protokoll bei laufendem Spiel nicht anwächst.
        /// </summary>
        private void LogRunningGameChanges(ISet<string> runningGameIds)
        {
            foreach (var gameId in runningGameIds)
            {
                if (!_previouslyRunningGameIds.Contains(gameId))
                {
                    Logger.Log($"Spielzeiterfassung gestartet für: {DescribeGame(gameId)}");
                }
            }

            foreach (var gameId in _previouslyRunningGameIds)
            {
                if (!runningGameIds.Contains(gameId))
                {
                    Logger.Log($"Spielzeiterfassung beendet für: {DescribeGame(gameId)}");
                }
            }

            _previouslyRunningGameIds = new HashSet<string>(runningGameIds, StringComparer.Ordinal);
        }

        private string DescribeGame(string gameId)
        {
            var game = _matchIndex.GetGameById(gameId);
            return game == null ? gameId : $"{game.Name} ({gameId})";
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
                updatedGameNames.Add(game.Name);
                sessionUpdates.Add(new PlaySessionUpdate(gameId, game.Name, game.PlayTime, now));
            }

            return sessionUpdates;
        }
    }
}
