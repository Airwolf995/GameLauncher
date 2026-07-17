using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests
{
    public class PlayTimeConfigTests
    {
        [Fact]
        public async Task LoadAllGamesAsync_LoadsCurrentPlayTimeFormat()
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
                        "manual_test": {
                          "name": "Legacy Spiel",
                          "seconds": 120
                        }
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
        public async Task LoadAllGamesAsync_MigratesNumericLegacyPlayTime()
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
                          "id": "manual_legacy",
                          "name": "Altes Spiel",
                          "platform": "Manuell",
                          "path": "C:\\Test\\AltesSpiel.exe",
                          "install_directory": "C:\\Test",
                          "executable_name": "AltesSpiel",
                          "source": "Manuell",
                          "launch_type": "exe",
                          "is_manual": true
                        }
                      ],
                      "play_time": {
                        "manual_legacy": 987
                      },
                      "ui_settings": {}
                    }
                    """);

                using var manager = new GameManager(configPath);
                var games = await manager.LoadAllGamesAsync(loadSteamMetadataInBackground: false);
                var game = Assert.Single(games, g => g.Id == "manual_legacy");

                Assert.Equal(987, game.PlayTime);
                Assert.Equal(987, manager.Config.PlayTime["manual_legacy"].Seconds);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void ConfigService_SkipsOnlyInvalidPlayTimeEntries()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllText(configPath,
                    """
                    {
                      "play_time": {
                        "valid": { "name": "Gültig", "seconds": 42 },
                        "legacy": 84,
                        "invalid": "unbekannt"
                      },
                      "ui_settings": {}
                    }
                    """);

                using var configService = new Services.ConfigService(configPath);

                Assert.Equal(42, configService.Config.PlayTime["valid"].Seconds);
                Assert.Equal(84, configService.Config.PlayTime["legacy"].Seconds);
                Assert.False(configService.Config.PlayTime.ContainsKey("invalid"));
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void ConfigService_BacksUpInvalidConfigBeforeOverwritingIt()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            const string invalidJson = "{ nicht gültig";

            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllText(configPath, invalidJson);

                using (var configService = new Services.ConfigService(configPath))
                {
                    configService.SaveConfigImmediate(configService.Config);
                }

                var backupPath = Assert.Single(
                    Directory.GetFiles(tempRoot, "game_launcher_config.json.invalid-*.bak"));
                Assert.Equal(invalidJson, File.ReadAllText(backupPath));
                Assert.NotEqual(invalidJson, File.ReadAllText(configPath));
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfigImmediate_SupersedesPendingDebouncedSnapshot()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                using (var configService = new Services.ConfigService(configPath))
                {
                    configService.Config.Theme = "Blue";
                    configService.SaveConfig();

                    configService.Config.Theme = "Green";
                    configService.SaveConfigImmediate(configService.Config);
                }

                using var reloadedConfigService = new Services.ConfigService(configPath);
                Assert.Equal("Green", reloadedConfigService.Config.Theme);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfigImmediate_SerializesConcurrentWrites()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                using (var configService = new Services.ConfigService(configPath))
                {
                    Parallel.For(0, 32, index =>
                    {
                        var config = new GameConfig { Theme = $"Theme-{index}" };
                        configService.SaveConfigImmediate(config);
                    });
                }

                string json = File.ReadAllText(configPath);
                var savedConfig = System.Text.Json.JsonSerializer.Deserialize<GameConfig>(json);

                Assert.NotNull(savedConfig);
                Assert.StartsWith("Theme-", savedConfig.Theme);
                Assert.False(File.Exists(configPath + ".tmp"));
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfig_DebouncesSerializationUntilFlush()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            int serializationCount = 0;
            int countAfterInitialization;
            DateTime utcNow = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

            try
            {
                Directory.CreateDirectory(tempRoot);
                using (var configService = new Services.ConfigService(
                           configPath,
                           config =>
                           {
                               Interlocked.Increment(ref serializationCount);
                               return System.Text.Json.JsonSerializer.Serialize(config);
                           },
                           TimeSpan.FromHours(1).TotalMilliseconds,
                           () => utcNow))
                {
                    countAfterInitialization = serializationCount;
                    configService.Config.Theme = "Blue";
                    configService.SaveConfig();
                    configService.Config.Theme = "Green";
                    configService.SaveConfig();

                    Assert.Equal(countAfterInitialization, serializationCount);

                    utcNow = utcNow.AddHours(1);
                    configService.FlushPendingSave();

                    Assert.Equal(countAfterInitialization + 1, serializationCount);
                }

                Assert.Equal(countAfterInitialization + 1, serializationCount);
                using var reloadedConfigService = new Services.ConfigService(configPath);
                Assert.Equal("Green", reloadedConfigService.Config.Theme);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public async Task SaveConfig_DiscardsStaleSerializationWhenNewerVersionArrives()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            using var serializationStarted = new ManualResetEventSlim();
            using var continueSerialization = new ManualResetEventSlim();
            int blockSerialization = 0;
            DateTime utcNow = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

            try
            {
                Directory.CreateDirectory(tempRoot);
                using var configService = new Services.ConfigService(
                    configPath,
                    config =>
                    {
                        string capturedTheme = config.Theme;
                        if (Interlocked.Exchange(ref blockSerialization, 0) == 1)
                        {
                            serializationStarted.Set();
                            if (!continueSerialization.Wait(TimeSpan.FromSeconds(5)))
                            {
                                throw new TimeoutException("Die Testserialisierung wurde nicht freigegeben.");
                            }
                        }

                        return System.Text.Json.JsonSerializer.Serialize(
                            new GameConfig { Theme = capturedTheme });
                    },
                    TimeSpan.FromHours(1).TotalMilliseconds,
                    () => utcNow);

                configService.Config.Theme = "Purple";
                configService.SaveConfig();
                utcNow = utcNow.AddHours(1);
                Volatile.Write(ref blockSerialization, 1);

                Task staleFlush = Task.Run(configService.FlushPendingSave);
                bool didStartSerialization = serializationStarted.Wait(TimeSpan.FromSeconds(2));
                if (!didStartSerialization)
                {
                    continueSerialization.Set();
                }
                Assert.True(didStartSerialization);

                configService.Config.Theme = "Green";
                configService.SaveConfig();
                continueSerialization.Set();
                await staleFlush.WaitAsync(TimeSpan.FromSeconds(2));

                var afterStaleFlush = System.Text.Json.JsonSerializer.Deserialize<GameConfig>(
                    File.ReadAllText(configPath));
                Assert.NotEqual("Purple", afterStaleFlush?.Theme);

                utcNow = utcNow.AddHours(1);
                configService.FlushPendingSave();

                var savedConfig = System.Text.Json.JsonSerializer.Deserialize<GameConfig>(
                    File.ReadAllText(configPath));
                Assert.Equal("Green", savedConfig?.Theme);
            }
            finally
            {
                continueSerialization.Set();
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfig_StopsAutomaticRetriesAfterLimit()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            int serializationCount = 0;
            bool failSerialization = false;
            DateTime utcNow = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

            try
            {
                Directory.CreateDirectory(tempRoot);
                using var configService = new Services.ConfigService(
                    configPath,
                    config =>
                    {
                        Interlocked.Increment(ref serializationCount);
                        if (failSerialization)
                        {
                            throw new InvalidOperationException("Erwarteter Serialisierungsfehler im Test.");
                        }

                        return System.Text.Json.JsonSerializer.Serialize(config);
                    },
                    TimeSpan.FromHours(1).TotalMilliseconds,
                    () => utcNow);

                int countAfterInitialization = serializationCount;
                failSerialization = true;
                configService.SaveConfig();

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    utcNow = utcNow.AddHours(10);
                    configService.FlushPendingSave();
                }

                Assert.Equal(countAfterInitialization + 4, serializationCount);
                Assert.Equal(3, configService.AutomaticSaveRetryCount);
                Assert.False(configService.IsSaveTimerEnabled);

                failSerialization = false;
                configService.SaveConfig();

                Assert.Equal(0, configService.AutomaticSaveRetryCount);
                Assert.True(configService.IsSaveTimerEnabled);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void SaveConfig_QueuedOldFlushHonorsNewDebounceWindow()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            int serializationCount = 0;
            DateTime utcNow = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

            try
            {
                Directory.CreateDirectory(tempRoot);
                using var configService = new Services.ConfigService(
                    configPath,
                    config =>
                    {
                        Interlocked.Increment(ref serializationCount);
                        return System.Text.Json.JsonSerializer.Serialize(config);
                    },
                    TimeSpan.FromHours(1).TotalMilliseconds,
                    () => utcNow);

                int countAfterInitialization = serializationCount;
                configService.Config.Theme = "Purple";
                configService.SaveConfig();

                // Der erste Auftrag ist fällig und sein Callback könnte bereits
                // eingeplant sein, bevor die nächste Änderung den Timer neu startet.
                utcNow = utcNow.AddHours(1);
                configService.Config.Theme = "Green";
                configService.SaveConfig();

                configService.FlushPendingSave();

                Assert.Equal(countAfterInitialization, serializationCount);
                Assert.True(configService.IsSaveTimerEnabled);

                utcNow = utcNow.AddHours(1);
                configService.FlushPendingSave();

                Assert.Equal(countAfterInitialization + 1, serializationCount);
                using var reloadedConfigService = new Services.ConfigService(configPath);
                Assert.Equal("Green", reloadedConfigService.Config.Theme);
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
