using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLauncher.Services
{
    public sealed class ActiveGameTracker
    {
        private readonly Dictionary<string, DateTime> _firstSeenRunningAt = new(StringComparer.Ordinal);

        public string? UpdateAndSelectActiveGameId(IEnumerable<string> runningGameIds, DateTime now)
        {
            var runningSet = runningGameIds as HashSet<string> ??
                             new HashSet<string>(runningGameIds, StringComparer.Ordinal);

            foreach (var gameId in runningSet)
            {
                _firstSeenRunningAt.TryAdd(gameId, now);
            }

            var stoppedGameIds = _firstSeenRunningAt.Keys.Where(id => !runningSet.Contains(id)).ToArray();
            foreach (var gameId in stoppedGameIds)
            {
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
