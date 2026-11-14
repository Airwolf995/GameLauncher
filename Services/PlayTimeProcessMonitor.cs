using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GameLauncher.Services;

/// <summary>
/// Behält die erkannten Spielprozesse für die Sitzungsüberwachung. Eine neue
/// Prozesssuche ist nur zur Erkennung oder nach einem Prozessende nötig.
/// </summary>
internal sealed class PlayTimeProcessMonitor : IDisposable
{
    private readonly Func<Process[]> _getProcesses;
    private readonly Dictionary<int, TrackedProcess> _processes = new();

    public PlayTimeProcessMonitor() : this(Process.GetProcesses) { }

    internal PlayTimeProcessMonitor(Func<Process[]> getProcesses) => _getProcesses = getProcesses;

    public Dictionary<string, DateTime> Poll(
        PlayTimeMatchIndex index,
        ISet<string> ignoredProcesses,
        bool discover,
        DateTime now)
    {
        foreach (var (id, tracked) in _processes.ToArray())
        {
            if (IsIgnored(tracked.Name, ignoredProcesses) || !IsRunning(tracked.Process))
            {
                // Ein Launcher kann gerade an den eigentlichen Spielprozess
                // übergeben haben. Deshalb sofort nach einem Nachfolger suchen.
                discover = true;
                tracked.Process.Dispose();
                _processes.Remove(id);
            }
        }

        if (discover)
        {
            Discover(index, ignoredProcesses, now);
        }

        var runningGames = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var tracked in _processes.Values)
        {
            if (!runningGames.TryGetValue(tracked.GameId, out var startedAt) || tracked.StartedAt < startedAt)
            {
                runningGames[tracked.GameId] = tracked.StartedAt;
            }
        }
        return runningGames;
    }

    private void Discover(PlayTimeMatchIndex index, ISet<string> ignoredProcesses, DateTime now)
    {
        foreach (var process in _getProcesses())
        {
            bool retained = false;
            try
            {
                if (_processes.ContainsKey(process.Id)) continue;
                string name = process.ProcessName;
                if (IsIgnored(name, ignoredProcesses)) continue;

                if (!index.TryMatchProcessByName(name, out string gameId))
                {
                    string path = PlayTimeMatchIndex.NormalizePath(
                        ProcessPathReader.TryGetExecutablePath(process.Id) ?? string.Empty);
                    if (string.IsNullOrEmpty(path) || !index.TryMatchProcessByPath(path, out gameId)) continue;
                }

                // HasExited öffnet zugleich das Handle des konkreten Prozesses.
                // Eine später wiederverwendete PID zählt damit nicht weiter.
                if (!IsRunning(process)) continue;
                DateTime startedAt;
                try { startedAt = process.StartTime; }
                catch (System.ComponentModel.Win32Exception) { startedAt = now; }
                _processes.Add(process.Id, new(process, gameId, name, startedAt));
                retained = true;
            }
            catch (InvalidOperationException)
            {
                // Zwischen Auflistung und Abfrage beendet.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows erlaubt den Zugriff auf diesen Prozess nicht.
            }
            finally
            {
                if (!retained) process.Dispose();
            }
        }
    }

    private static bool IsRunning(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool IsIgnored(string name, ISet<string> ignoredProcesses) =>
        string.IsNullOrWhiteSpace(name) || ignoredProcesses.Contains(name) || ignoredProcesses.Contains(name + ".exe");

    public void Dispose()
    {
        foreach (var tracked in _processes.Values) tracked.Process.Dispose();
        _processes.Clear();
    }

    private sealed record TrackedProcess(Process Process, string GameId, string Name, DateTime StartedAt);
}
