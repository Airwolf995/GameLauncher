using System;

namespace GameLauncher.Services
{
    /// <summary>
    /// Begrenzt Sensorabrufe und Wiederholungsversuche auf das vorgegebene Intervall.
    /// </summary>
    internal sealed class SensorUpdateThrottle
    {
        private readonly TimeSpan _minimumInterval;
        private readonly Func<DateTime> _utcNow;
        private DateTime? _lastUpdateUtc;

        public SensorUpdateThrottle(TimeSpan minimumInterval, Func<DateTime>? utcNow = null)
        {
            if (minimumInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumInterval),
                    "Das Mindestintervall darf nicht negativ sein.");
            }

            _minimumInterval = minimumInterval;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Meldet, ob jetzt ein Sensor-Update fällig ist. Ein <c>true</c> gilt als
        /// verbraucht: der Aufrufer muss das Update dann auch durchführen.
        /// </summary>
        public bool ShouldUpdate()
        {
            DateTime now = _utcNow();

            if (_lastUpdateUtc.HasValue)
            {
                TimeSpan sinceLastUpdate = now - _lastUpdateUtc.Value;

                // Eine rückwärts laufende Uhr (Zeitumstellung, NTP-Korrektur) darf
                // Updates nicht blockieren, bis die Zeit wieder aufgeholt hat.
                if (sinceLastUpdate >= TimeSpan.Zero && sinceLastUpdate < _minimumInterval)
                {
                    return false;
                }
            }

            _lastUpdateUtc = now;
            return true;
        }
    }
}
