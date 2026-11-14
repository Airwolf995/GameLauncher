using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLauncher.Services;

/// <summary>
/// Misst bestätigte Spielsitzungen mit einer monotonen Uhr. Bruchteile bleiben
/// bis zum nächsten Durchlauf erhalten; beim Sitzungsende entfällt nur die
/// letzte unvollständige Sekunde. Vor der ersten Erkennung wird nichts gebucht.
/// </summary>
internal sealed class PlayTimeSessionClock(TimeProvider timeProvider)
{
    private readonly Dictionary<string, TimeSpan> _remainders = new(StringComparer.Ordinal);
    private long _lastTimestamp = timeProvider.GetTimestamp();

    public Dictionary<string, int> Update(ISet<string> runningGameIds, bool resetBaseline = false)
    {
        long timestamp = timeProvider.GetTimestamp();
        TimeSpan elapsed = resetBaseline ? TimeSpan.Zero : timeProvider.GetElapsedTime(_lastTimestamp, timestamp);
        _lastTimestamp = timestamp;
        var addedSeconds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string gameId in _remainders.Keys.ToArray())
        {
            TimeSpan duration = _remainders[gameId] + elapsed;
            int seconds = (int)duration.TotalSeconds;
            if (seconds > 0)
            {
                addedSeconds[gameId] = seconds;
            }

            if (runningGameIds.Contains(gameId))
            {
                _remainders[gameId] = duration - TimeSpan.FromSeconds(seconds);
            }
            else
            {
                _remainders.Remove(gameId);
            }
        }

        foreach (string gameId in runningGameIds)
        {
            _remainders.TryAdd(gameId, TimeSpan.Zero);
        }

        return addedSeconds;
    }

    public void Clear()
    {
        _remainders.Clear();
        _lastTimestamp = timeProvider.GetTimestamp();
    }
}
