using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests
{
    public class PlayTimeHistoryTests
    {
        private static readonly DateTime Today = new(2026, 9, 24, 20, 0, 0, DateTimeKind.Local);

        private static Dictionary<string, Dictionary<string, int>> NewHistory() => new();

        [Fact]
        public void Add_SumsStepsOfTheSameDay()
        {
            var history = NewHistory();

            PlayTimeHistory.Add(history, "steam:1", Today, 10);
            PlayTimeHistory.Add(history, "steam:1", Today.AddMinutes(1), 10);

            Assert.Equal(20, PlayTimeHistory.GetDailySeconds(history["steam:1"], Today).Last());
        }

        /// <summary>
        /// Eine Sitzung über Mitternacht zählt je Schritt zu dem Tag, an dem er lag.
        /// </summary>
        [Fact]
        public void Add_SplitsASessionAcrossMidnight()
        {
            var history = NewHistory();
            var beforeMidnight = new DateTime(2026, 9, 23, 23, 59, 55, DateTimeKind.Local);

            PlayTimeHistory.Add(history, "steam:1", beforeMidnight, 10);
            PlayTimeHistory.Add(history, "steam:1", beforeMidnight.AddSeconds(10), 10);

            var daily = PlayTimeHistory.GetDailySeconds(history["steam:1"], Today);
            Assert.Equal(10, daily[^2]);
            Assert.Equal(10, daily[^1]);
        }

        [Fact]
        public void Add_KeepsGamesRunningAtTheSameTimeApart()
        {
            var history = NewHistory();

            PlayTimeHistory.Add(history, "steam:1", Today, 10);
            PlayTimeHistory.Add(history, "epic:2", Today, 10);

            Assert.Equal(10, PlayTimeHistory.GetDailySeconds(history["steam:1"], Today).Sum());
            Assert.Equal(10, PlayTimeHistory.GetDailySeconds(history["epic:2"], Today).Sum());
        }

        [Fact]
        public void GetDailySeconds_ReturnsFourteenDaysFromOldestToToday()
        {
            var history = NewHistory();
            PlayTimeHistory.Add(history, "steam:1", Today.AddDays(-13), 30);
            PlayTimeHistory.Add(history, "steam:1", Today.AddDays(-1), 60);

            var daily = PlayTimeHistory.GetDailySeconds(history["steam:1"], Today);

            Assert.Equal(PlayTimeHistory.RetainedDays, daily.Length);
            Assert.Equal(30, daily[0]);
            Assert.Equal(60, daily[^2]);
            Assert.Equal(0, daily[^1]);
            Assert.Equal(90, daily.Sum());
        }

        [Fact]
        public void GetDailySeconds_WithoutHistory_IsAllZero()
        {
            var daily = PlayTimeHistory.GetDailySeconds(null, Today);

            Assert.Equal(PlayTimeHistory.RetainedDays, daily.Length);
            Assert.All(daily, seconds => Assert.Equal(0, seconds));
        }

        [Fact]
        public void RemoveExpired_KeepsExactlyTheLastFourteenDays()
        {
            var history = NewHistory();
            PlayTimeHistory.Add(history, "steam:1", Today.AddDays(-14), 10);
            PlayTimeHistory.Add(history, "steam:1", Today.AddDays(-13), 20);
            PlayTimeHistory.Add(history, "steam:1", Today, 30);

            PlayTimeHistory.RemoveExpired(history, Today);

            Assert.Equal(2, history["steam:1"].Count);
            Assert.Equal(50, PlayTimeHistory.GetDailySeconds(history["steam:1"], Today).Sum());
        }

        [Fact]
        public void RemoveExpired_DropsGamesWithoutRecentDaysAndUnreadableEntries()
        {
            var history = NewHistory();
            PlayTimeHistory.Add(history, "steam:old", Today.AddDays(-30), 10);
            history["steam:broken"] = new Dictionary<string, int> { ["kein-datum"] = 10 };
            history["steam:empty"] = null!;

            PlayTimeHistory.RemoveExpired(history, Today);

            Assert.Empty(history);
        }

        [Fact]
        public void UpdatePlaySessions_PersistsDailyPlayTimeAcrossRestart()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                using (var manager = new GameManager(configPath))
                {
                    manager.UpdatePlaySessions(new[]
                    {
                        new PlaySessionUpdate("steam:123", "Portal 2", 600, Today.AddDays(-20), 10),
                    });
                    manager.UpdatePlaySessions(new[]
                    {
                        new PlaySessionUpdate("steam:123", "Portal 2", 610, Today.AddDays(-1), 10),
                        new PlaySessionUpdate("steam:123", "Portal 2", 620, Today, 10),
                    });
                }

                using (var reloaded = new GameManager(configPath))
                {
                    var daily = reloaded.GetDailyPlayTime("steam:123", Today);

                    Assert.Equal(10, daily[^2]);
                    Assert.Equal(10, daily[^1]);
                    Assert.Equal(20, daily.Sum());
                    Assert.Equal(1, reloaded.ReadConfig(config => config.PlayTimeByDay.Count));
                    Assert.Equal(2, reloaded.ReadConfig(config => config.PlayTimeByDay["steam:123"].Count));
                }
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

        /// <summary>
        /// Auch ohne Spielsitzung dürfen Tage außerhalb der 14 Tage nicht in der
        /// Konfiguration bleiben: Das Laden beim Start räumt sie auf.
        /// </summary>
        [Fact]
        public void LoadingAConfig_RemovesExpiredDaysWithoutPlaying()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            string oldDay = DateTime.Today.AddDays(-PlayTimeHistory.RetainedDays).ToString("yyyy-MM-dd");
            string recentDay = DateTime.Today.AddDays(-(PlayTimeHistory.RetainedDays - 1)).ToString("yyyy-MM-dd");

            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllText(configPath, $$"""
                    {
                      "play_time_by_day": {
                        "steam:old": { "{{oldDay}}": 600 },
                        "steam:recent": { "{{oldDay}}": 600, "{{recentDay}}": 300 }
                      }
                    }
                    """);

                using var configService = new ConfigService(configPath);
                var history = configService.Config.PlayTimeByDay;

                Assert.False(history.ContainsKey("steam:old"));
                Assert.Equal(new[] { recentDay }, history["steam:recent"].Keys);
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

        [Fact]
        public void LoadingAConfigWithoutHistory_StartsEmpty()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllText(configPath, """{ "play_time_by_day": null, "theme": "Blue" }""");

                using var manager = new GameManager(configPath);

                Assert.All(manager.GetDailyPlayTime("steam:123", Today), seconds => Assert.Equal(0, seconds));
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
