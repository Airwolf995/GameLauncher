using System;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class SensorUpdateThrottleTests
    {
        private static readonly DateTime Start = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ShouldUpdate_FirstCallIsAlwaysDue()
        {
            var clock = new TestClock(Start);
            var throttle = new SensorUpdateThrottle(TimeSpan.FromSeconds(1), clock.Now);

            Assert.True(throttle.ShouldUpdate());
        }

        /// <summary>
        /// Der eigentliche Zweck: die Einzelabfragen eines Overlay-Durchlaufs
        /// (CPU-Temperatur, GPU-Temperatur, GPU-Gesamtspeicher) folgen im
        /// Millisekundenabstand und duerfen sich ein Update teilen.
        /// </summary>
        [Fact]
        public void ShouldUpdate_CollapsesTheReadsOfASingleOverlayTick()
        {
            var clock = new TestClock(Start);
            var throttle = new SensorUpdateThrottle(TimeSpan.FromSeconds(1), clock.Now);

            int updates = 0;
            foreach (var _ in new[] { "CpuTemp", "GpuTemp", "GpuMemoryTotal" })
            {
                if (throttle.ShouldUpdate())
                {
                    updates++;
                }

                clock.Advance(TimeSpan.FromMilliseconds(3));
            }

            Assert.Equal(1, updates);
        }

        /// <summary>
        /// Jeder Overlay-Durchlauf muss frische Werte bekommen - das Intervall ist
        /// deshalb kuerzer als der 2-Sekunden-Takt des Overlays.
        /// </summary>
        [Fact]
        public void ShouldUpdate_EveryOverlayTickGetsAFreshUpdate()
        {
            var clock = new TestClock(Start);
            var throttle = new SensorUpdateThrottle(TimeSpan.FromSeconds(1), clock.Now);

            int updates = 0;
            for (int tick = 0; tick < 5; tick++)
            {
                // Zwei Einzelabfragen pro Durchlauf, wie im Overlay.
                if (throttle.ShouldUpdate()) updates++;
                clock.Advance(TimeSpan.FromMilliseconds(3));
                if (throttle.ShouldUpdate()) updates++;

                clock.Advance(TimeSpan.FromSeconds(2));
            }

            Assert.Equal(5, updates);
        }

        [Fact]
        public void ShouldUpdate_BlocksWithinTheIntervalAndAllowsAfterwards()
        {
            var clock = new TestClock(Start);
            var throttle = new SensorUpdateThrottle(TimeSpan.FromSeconds(1), clock.Now);

            Assert.True(throttle.ShouldUpdate());

            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.False(throttle.ShouldUpdate());

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(throttle.ShouldUpdate());
        }

        /// <summary>
        /// Eine rueckwaerts laufende Uhr (Zeitumstellung, NTP-Korrektur) darf die
        /// Sensoranzeige nicht einfrieren, bis die Zeit wieder aufgeholt hat.
        /// </summary>
        [Fact]
        public void ShouldUpdate_IsNotBlockedByAClockGoingBackwards()
        {
            var clock = new TestClock(Start);
            var throttle = new SensorUpdateThrottle(TimeSpan.FromSeconds(1), clock.Now);

            Assert.True(throttle.ShouldUpdate());

            clock.Advance(TimeSpan.FromHours(-1));

            Assert.True(throttle.ShouldUpdate());
        }

        [Fact]
        public void Constructor_RejectsNegativeInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SensorUpdateThrottle(TimeSpan.FromSeconds(-1)));
        }

        private sealed class TestClock
        {
            private DateTime _utcNow;

            public TestClock(DateTime utcNow) => _utcNow = utcNow;

            public DateTime Now() => _utcNow;

            public void Advance(TimeSpan delta) => _utcNow += delta;
        }
    }
}
