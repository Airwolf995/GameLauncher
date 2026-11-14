using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
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
        private const int TickIntervalSeconds = 2;
        private const int IdleTickIntervalSeconds = 30;
        private static readonly TimeSpan LaunchDetectionTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(1);
        private readonly TimeProvider _timeProvider;
        private readonly PlayTimeSessionClock _sessionClock;
        private readonly PlayTimeProcessMonitor _processMonitor = new();
        private readonly Dictionary<string, long> _pendingLaunches = new(StringComparer.Ordinal);
        private long? _lastDiscovery;
        private long _lastPersist;
        private int _resetTiming;
        private readonly GameManager _gameManager;
        private readonly IEnumerable<Game> _games;
        private readonly INotifyCollectionChanged? _observableGames;
        private readonly System.Timers.Timer _timer;
        private readonly Action? _tickBody;
        private readonly object _lifecycleSync = new();
        private readonly PlayTimeMatchIndex _matchIndex = new();
        private readonly ActiveGameTracker _activeGameTracker = new();
        private int _isTickRunning;
        private volatile bool _indexDirty;
        private TaskCompletionSource<bool> _tickCompleted = CreateCompletedTickSource();
        private bool _isRunning;
        private bool _disposed;

        /// <summary>
        /// Zuletzt erkannte Spiele. Dient zur Protokollierung der Wechsel und
        /// dazu, während des Startfensters nur nach fehlenden Spielen zu suchen.
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

        public Game? ActiveGame { get; private set; }
        public DateTime? SessionStartTime { get; private set; }

        public PlayTimeService(GameManager gameManager, IEnumerable<Game> games)
            : this(gameManager, games, null)
        {
        }

        internal PlayTimeService(
            GameManager gameManager,
            IEnumerable<Game> games,
            Action? tickBody,
            TimeProvider? timeProvider = null)
        {
            _gameManager = gameManager;
            _games = games;
            _tickBody = tickBody;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _sessionClock = new PlayTimeSessionClock(_timeProvider);

            // Zwei verschiedene Tatsachen machen den Index veraltet, und sie werden
            // über zwei verschiedene Wege gemeldet:
            //
            // GamesUpdated meldet Änderungen am Zustand vorhandener Spiele. Das
            // Ereignis lässt die Oberfläche allerdings die gesamte Bibliothek neu
            // laden und animieren, weshalb Hinzufügen, Importieren und Löschen
            // manueller Spiele es bewusst auslassen und ihre Änderung direkt in die
            // Sammlung schreiben.
            //
            // Deshalb wird zusätzlich die Sammlung selbst beobachtet: Sie ist die
            // Quelle, aus der der Index ohnehin gelesen wird, und meldet jede
            // Änderung ihres Bestands von sich aus. Damit kann kein Aufrufer die
            // Aktualisierung vergessen, ohne dass die Oberfläche etwas davon merkt.
            _gameManager.GamesUpdated += OnGamesUpdated;

            if (_games is INotifyCollectionChanged observableGames)
            {
                _observableGames = observableGames;
                _observableGames.CollectionChanged += OnGamesCollectionChanged;
            }

            _timer = new System.Timers.Timer(TickIntervalSeconds * 1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            lock (_lifecycleSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_isRunning) return;
                RebuildMatchIndex();
                _sessionClock.Clear();
                _lastDiscovery = null;
                _lastPersist = _timeProvider.GetTimestamp();
                _pendingLaunches.Clear();
                _previouslyRunningGameIds.Clear();
                _timer.Interval = TickIntervalSeconds * 1000;
                Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
                _isRunning = true;
                _timer.Start();
                Logger.Log($"PlayTimeService started ({TickIntervalSeconds}s interval, tracking in seconds).");
            }
        }

        public async Task StopAsync()
        {
            Task runningTick;
            lock (_lifecycleSync)
            {
                if (_disposed) return;
                _isRunning = false;
                _timer.Stop();
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                runningTick = _tickCompleted.Task;
            }
            await runningTick;
            // Den Rest seit dem letzten Tick noch vor dem Speichern beim
            // Herunterfahren buchen. Kein Prozess aus der letzten Sitzung bleibt offen.
            RecordPlayTime(_timeProvider.GetLocalNow().DateTime, new(StringComparer.Ordinal), new());
            _processMonitor.Dispose();
            _gameManager.SaveConfig();
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
            if (_observableGames != null)
            {
                _observableGames.CollectionChanged -= OnGamesCollectionChanged;
            }

            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _processMonitor.Dispose();
            _timer.Dispose();
            Logger.Log("PlayTimeService stopped.");
        }

        /// <summary>
        /// Ein erfolgreicher Startauftrag bestätigt noch keinen laufenden
        /// Spielprozess. Bis zur Erkennung kurz prüfen, ohne Wartezeit zu buchen.
        /// </summary>
        public void NotifyGameLaunched(Game game)
        {
            if (!game.SupportsPlayTimeTracking) return;
            lock (_lifecycleSync)
            {
                if (_disposed || !_isRunning) return;
                _pendingLaunches[game.Id] = _timeProvider.GetTimestamp();
                _timer.Interval = TickIntervalSeconds * 1000;
            }
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            // Die monotone Uhr läuft je nach System auch im Standby weiter.
            // Das erste Intervall danach darf deshalb keine Spielzeit erzeugen.
            if (e.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume)
                Interlocked.Exchange(ref _resetTiming, 1);
        }

        private void OnGamesUpdated(object? sender, EventArgs e)
        {
            _indexDirty = true;
        }

        /// <summary>
        /// Ein Spiel ist hinzugekommen oder verschwunden. Ohne diese Meldung liefe
        /// der Index mit dem alten Bestand weiter: ein gelöschtes Spiel sammelte
        /// weiter Spielzeit und legte damit die gerade entfernten Einträge in der
        /// Konfiguration neu an, ein hinzugefügtes bekäme dagegen keine.
        /// </summary>
        private void OnGamesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

                var now = _timeProvider.GetLocalNow().DateTime;

                // Neu aufbauen nur, wenn sich die Spieleliste geändert hat (ereignisbasiert).
                if (_indexDirty)
                {
                    RebuildMatchIndex();
                }

                long timestamp = _timeProvider.GetTimestamp();
                bool awaitingLaunch;
                lock (_lifecycleSync)
                {
                    foreach (var id in _pendingLaunches.Keys.ToArray())
                    {
                        if (_timeProvider.GetElapsedTime(_pendingLaunches[id], timestamp) >= LaunchDetectionTimeout)
                            _pendingLaunches.Remove(id);
                    }
                    // Den Startauftrag bis zum ursprünglichen Zeitlimit behalten:
                    // Der erste Treffer kann nur das vorgeschaltete Startprogramm
                    // sein. Solange sein Prozess läuft, genügt die direkte Prüfung.
                    awaitingLaunch = _pendingLaunches.Keys.Any(id => !_previouslyRunningGameIds.Contains(id));
                }
                bool discover = awaitingLaunch || !_lastDiscovery.HasValue ||
                    _timeProvider.GetElapsedTime(_lastDiscovery.Value, timestamp).TotalSeconds >= IdleTickIntervalSeconds;
                var ignoredProcesses = new HashSet<string>(
                    _gameManager.GetIgnoredProcessesSnapshot(), StringComparer.OrdinalIgnoreCase);
                ignoredProcesses.UnionWith(WindowsSystemProcesses);
                var runningGameStartedAt = _processMonitor.Poll(_matchIndex, ignoredProcesses, discover, now);
                if (discover) _lastDiscovery = timestamp;
                var runningGameIds = new HashSet<string>(runningGameStartedAt.Keys, StringComparer.Ordinal);

                LogRunningGameChanges(runningGameIds);
                ApplyTickInterval(runningGameIds.Count > 0 || awaitingLaunch);
                RecordPlayTime(now, runningGameIds, runningGameStartedAt);
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

        private void RebuildMatchIndex()
        {
            // Vor dem Snapshot quittieren: Änderungen während des Aufbaus
            // müssen den nächsten Durchlauf weiterhin als veraltet markieren.
            _indexDirty = false;
            try
            {
                var gamesSnapshot = CaptureGamesSnapshotOnUiThread();
                _matchIndex.Rebuild(gamesSnapshot);
                _processMonitor.Dispose();
                _lastDiscovery = null;
            }
            catch
            {
                // Wie bisher bleibt ein fehlgeschlagener Aufbau wiederholbar.
                _indexDirty = true;
                throw;
            }
        }

        /// <summary>
        /// Verbucht die Spielzeit der laufenden Spiele und speichert sie in
        /// regelmäßigen Abständen sowie am Ende einer Sitzung.
        /// </summary>
        private void RecordPlayTime(
            DateTime now,
            HashSet<string> runningGameIds,
            Dictionary<string, DateTime> runningGameStartedAt)
        {
            var activeGameId = _activeGameTracker.UpdateAndSelectActiveGameId(runningGameIds, now);
            DateTime? activeGameStartedAt = activeGameId != null && runningGameStartedAt.TryGetValue(activeGameId, out var startedAt)
                ? startedAt
                : null;
            var addedSeconds = _sessionClock.Update(runningGameIds, Interlocked.Exchange(ref _resetTiming, 0) != 0);
            var sessionUpdates = new List<PlaySessionUpdate>();
            bool hadTrackedSession = ActiveGame != null || SessionStartTime != null;
            if (runningGameIds.Count > 0 || hadTrackedSession || addedSeconds.Count > 0)
            {
                sessionUpdates = ApplyPlayTimeUpdatesOnUiThread(now, activeGameId, activeGameStartedAt, addedSeconds);
            }

            long timestamp = _timeProvider.GetTimestamp();
            bool sessionEnded = addedSeconds.Keys.Any(id => !runningGameIds.Contains(id)) ||
                (hadTrackedSession && runningGameIds.Count == 0);
            bool persistConfig = sessionEnded || _timeProvider.GetElapsedTime(_lastPersist, timestamp) >= PersistInterval;
            if (sessionUpdates.Count > 0)
            {
                _gameManager.UpdatePlaySessions(sessionUpdates, persistConfig);
            }
            else if (sessionEnded)
            {
                _gameManager.SaveConfig();
            }
            if (persistConfig) _lastPersist = timestamp;
        }

        /// <summary>
        /// Das Scan-Intervall abhängig davon, ob gerade ein Spiel läuft.
        /// </summary>
        internal static int GetTickIntervalSeconds(bool anyGameRunning) =>
            anyGameRunning ? TickIntervalSeconds : IdleTickIntervalSeconds;

        internal double CurrentTickIntervalMs => _timer.Interval;

        private void ApplyTickInterval(bool anyGameRunning)
        {
            double desiredIntervalMs = GetTickIntervalSeconds(anyGameRunning) * 1000d;

            lock (_lifecycleSync)
            {
                // Ein Startauftrag kann nach dem Snapshot dieses Ticks eintreffen.
                // Er darf nicht wieder auf den Leerlauftakt zurückgesetzt werden.
                if (_pendingLaunches.Count > 0) desiredIntervalMs = TickIntervalSeconds * 1000;
                if (_disposed || Math.Abs(_timer.Interval - desiredIntervalMs) < 1d)
                {
                    return;
                }

                _timer.Interval = desiredIntervalMs;
                Logger.Log(
                    $"Spielzeit-Prüfintervall auf {desiredIntervalMs / 1000d:0}s gesetzt.");
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

        private List<PlaySessionUpdate> ApplyPlayTimeUpdatesOnUiThread(
            DateTime now,
            string? activeGameId,
            DateTime? activeGameStartedAt,
            IReadOnlyDictionary<string, int> addedSeconds)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return ApplyPlayTimeUpdates(now, activeGameId, activeGameStartedAt, addedSeconds);
            }

            return dispatcher.Invoke(() => ApplyPlayTimeUpdates(now, activeGameId, activeGameStartedAt, addedSeconds));
        }

        private List<PlaySessionUpdate> ApplyPlayTimeUpdates(
            DateTime now,
            string? activeGameId,
            DateTime? activeGameStartedAt,
            IReadOnlyDictionary<string, int> addedSeconds)
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

            // Auch das letzte Intervall eines gerade beendeten Spiels buchen.
            foreach (var (gameId, seconds) in addedSeconds)
            {
                var game = _matchIndex.GetGameById(gameId);
                if (game == null)
                {
                    continue;
                }

                game.PlayTime += seconds;
                game.LastPlayed = now;
                sessionUpdates.Add(new PlaySessionUpdate(gameId, game.Name, game.PlayTime, now, seconds));
            }

            return sessionUpdates;
        }
    }
}
