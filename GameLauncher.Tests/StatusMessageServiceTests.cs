using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Services.MainWindow;
using Xunit;

namespace GameLauncher.Tests
{
    public class StatusMessageServiceTests
    {
        private const int ErsteAnzeigedauerMs = 200;
        private const int ZweiteAnzeigedauerMs = 50;

        /// <summary>
        /// Der Test wartet auf die Wiederherstellung, statt ihr eine feste Frist
        /// zu setzen. Zuvor bekam der 50-Millisekunden-Zeitgeber 150 Millisekunden
        /// Luft; auf einem ausgelasteten Rechner reichte das nicht, und der Test
        /// schlug gelegentlich fehl, obwohl der Dienst richtig arbeitete.
        /// </summary>
        [Fact]
        public async Task ShowStatus_OnlyLatestMessageRestoresDefault()
        {
            var messages = new List<string>();
            var restoreCount = 0;
            var wiederhergestellt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var service = new StatusMessageService(
                message => messages.Add(message),
                () =>
                {
                    Interlocked.Increment(ref restoreCount);
                    wiederhergestellt.TrySetResult();
                });

            service.ShowStatus("first", ErsteAnzeigedauerMs);
            await Task.Delay(ZweiteAnzeigedauerMs);
            service.ShowStatus("second", ZweiteAnzeigedauerMs);

            var beendet = await Task.WhenAny(wiederhergestellt.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(
                beendet == wiederhergestellt.Task,
                "Der Standardtext wurde auch nach zehn Sekunden nicht wiederhergestellt.");

            // Danach noch so lange warten, wie die erste Meldung gebraucht haette:
            // Nur so zeigt sich, dass ihre Wiederherstellung wirklich entfaellt
            // und nicht bloss noch aussteht.
            await Task.Delay(ErsteAnzeigedauerMs);

            Assert.Equal(new[] { "first", "second" }, messages);
            Assert.Equal(1, restoreCount);
        }

        [Fact]
        public async Task Dispose_CancelsPendingRestore()
        {
            var restored = false;
            using var service = new StatusMessageService(
                _ => { },
                () => restored = true);

            service.ShowStatus("temp", 200);
            service.Dispose();

            await Task.Delay(250);

            Assert.False(restored);
        }
    }
}
