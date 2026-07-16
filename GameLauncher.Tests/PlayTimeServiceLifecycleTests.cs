using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class PlayTimeServiceLifecycleTests
    {
        [Fact]
        public async Task StopAsync_WaitsForRunningTickAndRejectsNewTicks()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "GameLauncherTests",
                Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            using var tickStarted = new ManualResetEventSlim();
            using var releaseTick = new ManualResetEventSlim();
            int tickRuns = 0;

            try
            {
                Directory.CreateDirectory(tempRoot);
                using var manager = new GameManager(configPath);
                using var service = new PlayTimeService(
                    manager,
                    Array.Empty<Game>(),
                    () =>
                    {
                        Interlocked.Increment(ref tickRuns);
                        tickStarted.Set();
                        if (!releaseTick.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Der Test-Tick wurde nicht freigegeben.");
                        }
                    });

                service.Start();
                Task runningTick = Task.CompletedTask;
                Task? stopTask = null;
                try
                {
                    runningTick = Task.Run(service.RunTick);
                    bool didStartTick = tickStarted.Wait(TimeSpan.FromSeconds(2));
                    Assert.True(didStartTick);

                    stopTask = service.StopAsync();
                    Assert.False(stopTask.IsCompleted);
                }
                finally
                {
                    releaseTick.Set();
                    await runningTick.WaitAsync(TimeSpan.FromSeconds(2));
                }

                Assert.NotNull(stopTask);
                await stopTask.WaitAsync(TimeSpan.FromSeconds(2));

                service.RunTick();

                Assert.Equal(1, Volatile.Read(ref tickRuns));
            }
            finally
            {
                releaseTick.Set();
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
