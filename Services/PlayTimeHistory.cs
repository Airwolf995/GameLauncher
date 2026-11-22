using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameLauncher.Services
{
    /// <summary>
    /// Spielzeit je Spiel und Kalendertag für den Verlauf in der Detailansicht.
    ///
    /// Gezählt wird pro Erfassungsschritt statt pro Sitzung: So braucht es keine
    /// Erkennung, wann eine Sitzung endet, ein Absturz lässt keine halb offene
    /// Sitzung zurück. Ein Erfassungsschritt über Mitternacht wird auf beide
    /// Tage verteilt. Aufbewahrt werden nur die letzten
    /// <see cref="RetainedDays"/> Tage; die Gesamtspielzeit bleibt davon unberührt.
    /// </summary>
    internal static class PlayTimeHistory
    {
        public const int RetainedDays = 14;

        private const string DayFormat = "yyyy-MM-dd";

        public static void Add(
            Dictionary<string, Dictionary<string, int>> history,
            string gameId,
            DateTime playedAt,
            int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            if (!history.TryGetValue(gameId, out var days) || days == null)
            {
                days = new Dictionary<string, int>(StringComparer.Ordinal);
                history[gameId] = days;
            }

            DateTime day = playedAt.Date;
            int secondsOnDay = Math.Min(seconds, (int)playedAt.TimeOfDay.TotalSeconds);
            for (int daysBack = 0; seconds > 0 && daysBack < RetainedDays; daysBack++)
            {
                if (secondsOnDay > 0)
                {
                    string key = ToKey(day);
                    days[key] = days.TryGetValue(key, out int existing) ? existing + secondsOnDay : secondsOnDay;
                    seconds -= secondsOnDay;
                }

                if (seconds == 0 || day == DateTime.MinValue)
                {
                    break;
                }

                day = day.AddDays(-1);
                secondsOnDay = Math.Min(seconds, (int)TimeSpan.FromDays(1).TotalSeconds);
            }
        }

        /// <summary>
        /// Entfernt Tage vor dem Aufbewahrungszeitraum, unlesbare Einträge und
        /// Spiele ohne verbleibende Tage.
        /// </summary>
        public static void RemoveExpired(Dictionary<string, Dictionary<string, int>> history, DateTime today)
        {
            DateTime firstRetainedDay = today.Date.AddDays(-(RetainedDays - 1));

            foreach (var gameId in history.Keys.ToList())
            {
                var days = history[gameId];
                if (days == null)
                {
                    history.Remove(gameId);
                    continue;
                }

                foreach (var day in days.Keys.ToList())
                {
                    if (!TryParseKey(day, out var date) || date < firstRetainedDay)
                    {
                        days.Remove(day);
                    }
                }

                if (days.Count == 0)
                {
                    history.Remove(gameId);
                }
            }
        }

        /// <summary>
        /// Sekunden je Tag für die letzten <see cref="RetainedDays"/> Tage,
        /// vom ältesten bis einschließlich heute; Tage ohne Spielzeit sind 0.
        /// </summary>
        public static int[] GetDailySeconds(IReadOnlyDictionary<string, int>? days, DateTime today)
        {
            var result = new int[RetainedDays];
            if (days == null)
            {
                return result;
            }

            DateTime firstDay = today.Date.AddDays(-(RetainedDays - 1));
            for (int i = 0; i < RetainedDays; i++)
            {
                if (days.TryGetValue(ToKey(firstDay.AddDays(i)), out int seconds))
                {
                    result[i] = seconds;
                }
            }

            return result;
        }

        private static string ToKey(DateTime date) =>
            date.ToString(DayFormat, CultureInfo.InvariantCulture);

        private static bool TryParseKey(string key, out DateTime date) =>
            DateTime.TryParseExact(key, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
