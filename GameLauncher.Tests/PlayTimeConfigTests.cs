using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class PlayTimeConfigTests
    {
        [Fact]
        public async Task LoadAllGamesAsync_LoadsLegacyPlayTimeFormat()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                await File.WriteAllTextAsync(configPath,
                    """
                    {
                      "manual_games": [
                        {
                          "id": "manual_test",
                          "name": "Legacy Spiel",
                          "platform": "Manuell",
                          "path": "C:\\Test\\LegacySpiel.exe",
                          "args": "",
                          "install_directory": "C:\\Test",
                          "executable_name": "LegacySpiel",
                          "source": "Manuell",
                          "launch_type": "exe",
                          "is_manual": true,
                          "image_url": ""
                        }
                      ],
                      "play_time": {
                        "manual_test": 120
                      },
                      "ui_settings": {}
                    }
                    """);

                using var manager = new GameManager(configPath);
                var games = await manager.LoadAllGamesAsync(loadSteamMetadataInBackground: false);
                var game = Assert.Single(games, g => g.Id == "manual_test");

                Assert.Equal(120, game.PlayTime);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public async Task LoadAllGamesAsync_MigratesLegacyPlayTimeEntryToNamedObject()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                await File.WriteAllTextAsync(configPath,
                    """
                    {
                      "manual_games": [
                        {
                          "id": "manual_test",
                          "name": "Mein Testspiel",
                          "platform": "Manuell",
                          "path": "C:\\Test\\MeinTestspiel.exe",
                          "args": "",
                          "install_directory": "C:\\Test",
                          "executable_name": "MeinTestspiel",
                          "source": "Manuell",
                          "launch_type": "exe",
                          "is_manual": true,
                          "image_url": ""
                        }
                      ],
                      "play_time": {
                        "manual_test": 120
                      },
                      "ui_settings": {}
                    }
                    """);

                using (var manager = new GameManager(configPath))
                {
                    await manager.LoadAllGamesAsync(loadSteamMetadataInBackground: false);
                }

                var json = File.ReadAllText(configPath);

                Assert.Contains("\"manual_test\": {", json);
                Assert.Contains("\"name\": \"Mein Testspiel\"", json);
                Assert.Contains("\"seconds\": 120", json);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfig_WritesPlayTimeWithGameName()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                using (var manager = new GameManager(configPath))
                {
                    manager.UpdatePlaySessions(new[]
                    {
                        new PlaySessionUpdate("steam:123", "Portal 2", 345, new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Local))
                    });
                }

                var json = File.ReadAllText(configPath);

                Assert.Contains("\"steam:123\"", json);
                Assert.Contains("\"name\": \"Portal 2\"", json);
                Assert.Contains("\"seconds\": 345", json);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        private static string CreateTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
        }

        private static void CleanupTempRoot(string tempRoot)
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
