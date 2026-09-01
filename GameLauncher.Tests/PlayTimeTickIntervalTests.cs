using System;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests
{
    /// <summary>
    /// Das Scan-Intervall passt sich an: jeder Durchlauf legt mehrere hundert
    /// Prozessobjekte an, im Leerlauf ist daran aber nichts zu holen.
    /// </summary>
    public class PlayTimeTickIntervalTests
    {
        [Fact]
        public void GetTickIntervalSeconds_UsesTheShortIntervalWhileAGameIsRunning()
        {
            Assert.Equal(10, PlayTimeService.GetTickIntervalSeconds(anyGameRunning: true));
        }

        [Fact]
        public void GetTickIntervalSeconds_UsesTheLongIntervalWhenIdle()
        {
            Assert.Equal(30, PlayTimeService.GetTickIntervalSeconds(anyGameRunning: false));
        }

        /// <summary>
        /// Der erste Durchlauf muss im kurzen Takt kommen, sonst wuerde ein beim
        /// Start bereits laufendes Spiel erst nach dem langen Intervall auffallen.
        /// </summary>
        [Fact]
        public void Start_BeginsWithTheShortInterval()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "GameLauncherTests",
                Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                using var manager = new GameManager(configPath);
                using var service = new PlayTimeService(manager, Array.Empty<Game>(), () => { });

                service.Start();

                Assert.Equal(10_000d, service.CurrentTickIntervalMs);
            }
            finally
            {
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
