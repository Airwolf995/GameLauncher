using System;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;

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

        [Fact]
        public void RemoveManualGame_RemovesAllAssociatedStateAndManagedImage()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            var imagesDirectory = Path.Combine(tempRoot, "images");
            var imagePath = Path.Combine(imagesDirectory, "cover.jpg");

            try
            {
                Directory.CreateDirectory(imagesDirectory);
                File.WriteAllText(imagePath, "Bild");

                using var manager = new GameManager(configPath);
                var game = new Game
                {
                    Id = "manual_cleanup",
                    Name = "Aufräumtest",
                    IsManual = true,
                    ImageUrl = imagePath
                };

                manager.Config.ManualGames.Add(game);
                manager.Config.Favorites.Add(game.Id);
                manager.Config.LastPlayed[game.Id] = DateTime.Now;
                manager.Config.PlayTime[game.Id] = new PlayTimeEntry { Name = game.Name, Seconds = 120 };
                manager.Config.HiddenGames.Add(game.Id);
                manager.Config.GameTags[game.Id] = new() { "Test" };
                manager.Config.ImageOverrides[game.Id] = imagePath;

                manager.RemoveManualGame(game, notifyUI: false);

                Assert.DoesNotContain(manager.Config.ManualGames, entry => entry.Id == game.Id);
                Assert.DoesNotContain(game.Id, manager.Config.Favorites);
                Assert.False(manager.Config.LastPlayed.ContainsKey(game.Id));
                Assert.False(manager.Config.PlayTime.ContainsKey(game.Id));
                Assert.DoesNotContain(game.Id, manager.Config.HiddenGames);
                Assert.False(manager.Config.GameTags.ContainsKey(game.Id));
                Assert.False(manager.Config.ImageOverrides.ContainsKey(game.Id));
                Assert.False(File.Exists(imagePath));
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void RemoveManualGame_DoesNotDeleteImageFromSiblingDirectory()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            var siblingDirectory = Path.Combine(tempRoot, "images-backup");
            var imagePath = Path.Combine(siblingDirectory, "cover.jpg");

            try
            {
                Directory.CreateDirectory(siblingDirectory);
                File.WriteAllText(imagePath, "Bild");

                using var manager = new GameManager(configPath);
                var game = new Game
                {
                    Id = "manual_external_image",
                    Name = "Externer Bildtest",
                    IsManual = true,
                    ImageUrl = imagePath
                };
                manager.Config.ManualGames.Add(game);

                manager.RemoveManualGame(game, notifyUI: false);

                Assert.True(File.Exists(imagePath));
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        private static string CreateTempRoot() =>
            Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));

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
