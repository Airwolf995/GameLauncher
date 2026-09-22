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

        [Theory]
        [InlineData(2, true)]      // Datei nicht gefunden
        [InlineData(3, true)]      // Pfad nicht gefunden
        [InlineData(5, false)]     // Zugriff verweigert
        [InlineData(1223, false)]  // UAC-Abfrage abgelehnt
        public void IsMissingFileError_MeldetNurFehlendeDateiOderOrdner(int nativeErrorCode, bool expected)
        {
            var ex = new System.ComponentModel.Win32Exception(nativeErrorCode);

            Assert.Equal(expected, GameManager.IsMissingFileError(ex));
        }

        [Fact]
        public void IsMissingFileError_WertetAndereAusnahmenNichtAlsFehlendeDatei()
        {
            var ex = new InvalidOperationException("Could not find anything");

            Assert.False(GameManager.IsMissingFileError(ex));
        }

        [Fact]
        public void UpdateManualGame_UebernimmtEingabenUndBehaeltIdSowieSpielzeit()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            string newPath = Path.Combine(tempRoot, "Spiele", "Neu", "neu.exe");
            string coverPath = Path.Combine(tempRoot, "cover.png");

            try
            {
                string gameId;
                using (var manager = new GameManager(configPath))
                {
                    // URI und eigenes Bild, damit kein Symbol ins Benutzerprofil extrahiert wird.
                    var game = manager.AddManualGame("Alt", "steam://rungameid/1", "", coverPath, notifyUI: false);
                    gameId = game.Id;
                    manager.Config.PlayTime[gameId] = new PlayTimeEntry { Name = "Alt", Seconds = 3600 };
                    manager.Config.Favorites.Add(gameId);
                    int updateEvents = 0;
                    manager.GamesUpdated += (_, _) => updateEvents++;

                    manager.UpdateManualGame(game, "Neu", newPath, "-windowed");

                    Assert.Equal(gameId, game.Id);
                    Assert.Equal("Neu", game.Name);
                    Assert.Equal(newPath, game.Path);
                    Assert.Equal("-windowed", game.Args);
                    Assert.Equal("exe", game.LaunchType);
                    Assert.Equal(Path.GetDirectoryName(newPath), game.InstallDirectory);
                    Assert.Equal(1, updateEvents);
                }

                // Nach dem Neuladen muss die Änderung gespeichert sein, Spielzeit und Favorit unverändert.
                using var reloaded = new GameManager(configPath);
                var stored = Assert.Single(reloaded.Config.ManualGames, entry => entry.Id == gameId);
                Assert.Equal("Neu", stored.Name);
                Assert.Equal(newPath, stored.Path);
                Assert.Equal("-windowed", stored.Args);
                Assert.Equal(coverPath, stored.ImageUrl);
                Assert.Equal(3600, reloaded.Config.PlayTime[gameId].Seconds);
                Assert.Contains(gameId, reloaded.Config.Favorites);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Fact]
        public void UpdateManualGame_IgnoriertUnbekanntesSpiel()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                using var manager = new GameManager(configPath);
                var game = new Game { Id = "manual_unbekannt", Name = "Alt", Path = "steam://rungameid/1", IsManual = true };
                int updateEvents = 0;
                manager.GamesUpdated += (_, _) => updateEvents++;

                manager.UpdateManualGame(game, "Neu", "steam://rungameid/2", "");

                Assert.Equal("Alt", game.Name);
                Assert.Empty(manager.Config.ManualGames);
                Assert.Equal(0, updateEvents);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        [Theory]
        [InlineData("steam://rungameid/1", "Manuell", "uri")]
        [InlineData("com.epicgames.launcher://apps/abc?action=launch", "Epic Games", "uri")]
        [InlineData("battlenet://Pro", "Battle.net", "uri")]
        public void ApplyManualGameInput_StuftUriNachPlattformEin(string path, string expectedPlatform, string expectedLaunchType)
        {
            var game = new Game();

            GameManager.ApplyManualGameInput(game, "Spiel", path, "");

            Assert.Equal(path, game.Path);
            Assert.Equal(expectedPlatform, game.Platform);
            Assert.Equal(expectedLaunchType, game.LaunchType);
        }

        [Fact]
        public void ApplyManualGameInput_NormalisiertProgrammpfad()
        {
            var game = new Game();

            GameManager.ApplyManualGameInput(game, "Spiel", @"C:\Spiele\\Ordner\..\Spiel\spiel.exe", "");

            Assert.Equal(@"C:\Spiele\Spiel\spiel.exe", game.Path);
            Assert.Equal("Manuell", game.Platform);
            Assert.Equal("exe", game.LaunchType);
            Assert.Equal(@"C:\Spiele\Spiel", game.InstallDirectory);
        }
    }
}
