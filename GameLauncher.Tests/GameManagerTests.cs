using System;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class GameManagerTests
    {
        [Fact]
        public void Constructor_CreatesMissingConfigFileImmediately()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Assert.False(File.Exists(configPath));

                var manager = new GameManager(configPath);

                Assert.True(File.Exists(configPath));
                Assert.NotNull(manager.Config);

                var json = File.ReadAllText(configPath);
                Assert.Contains("\"ui_settings\"", json);
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
