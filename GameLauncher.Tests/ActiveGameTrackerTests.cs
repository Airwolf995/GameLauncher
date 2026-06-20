using System;
using GameLauncher.Services;
using Xunit;

namespace GameLauncher.Tests
{
    public class ActiveGameTrackerTests
    {
        [Fact]
        public void UpdateAndSelectActiveGameId_PrefersMostRecentlyStartedGame()
        {
            var tracker = new ActiveGameTracker();
            var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var t2 = t1.AddSeconds(10);

            var active1 = tracker.UpdateAndSelectActiveGameId(new[] { "g1" }, t1);
            var active2 = tracker.UpdateAndSelectActiveGameId(new[] { "g1", "g2" }, t2);

            Assert.Equal("g1", active1);
            Assert.Equal("g2", active2);
        }

        [Fact]
        public void UpdateAndSelectActiveGameId_ReturnsNullWhenNoGamesRunning()
        {
            var tracker = new ActiveGameTracker();
            var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

            tracker.UpdateAndSelectActiveGameId(new[] { "g1" }, t1);
            var active = tracker.UpdateAndSelectActiveGameId(Array.Empty<string>(), t1.AddSeconds(20));

            Assert.Null(active);
        }
    }
}
