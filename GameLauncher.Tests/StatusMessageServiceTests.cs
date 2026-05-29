using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Services.MainWindow;
using Xunit;

namespace GameLauncher.Tests
{
    public class StatusMessageServiceTests
    {
        [Fact]
        public async Task ShowStatus_OnlyLatestMessageRestoresDefault()
        {
            var messages = new List<string>();
            var restoreCount = 0;

            using var service = new StatusMessageService(
                message => messages.Add(message),
                () => Interlocked.Increment(ref restoreCount));

            service.ShowStatus("first", 200);
            await Task.Delay(50);
            service.ShowStatus("second", 50);

            await Task.Delay(200);

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
