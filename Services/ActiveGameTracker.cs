using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLauncher.Services
{
    public sealed class ActiveGameTracker
    {
        private readonly Dictionary<string, DateTime> _firstSeenRunningAt = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _lastSeenRunningAt = new(StringComparer.Ordinal);

        public string? UpdateAndSelectActiveGameId(IEnumerable<string> runningGameIds, DateTime now)
        {
            var runningSet = runningGameIds as HashSet<string> ??
                             new HashSet<string>(runningGameIds, StringComparer.Ordinal);

            foreach (var gameId in runningSet)
            {
                if (!_firstSeenRunningAt.ContainsKey(gameId))
                {
                    _firstSeenRunningAt[gameId] = now;
                }

                _lastSeenRunningAt[gameId] = now;
            }

            var stoppedGameIds = _lastSeenRunningAt.Keys.Where(id => !runningSet.Contains(id)).ToArray();
            foreach (var gameId in stoppedGameIds)
            {
                _lastSeenRunningAt.Remove(gameId);
                _firstSeenRunningAt.Remove(gameId);
            }

            if (runningSet.Count == 0)
            {
                return null;
            }

            return runningSet
                .OrderByDescending(id => _firstSeenRunningAt.TryGetValue(id, out var startedAt) ? startedAt : DateTime.MinValue)
                .ThenBy(id => id, StringComparer.Ordinal)
                .First();
        }
    }
}
