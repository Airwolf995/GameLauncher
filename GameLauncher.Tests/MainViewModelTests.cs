using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using GameLauncher.Models;
using GameLauncher.ViewModels;

namespace GameLauncher.Tests
{
    public class MainViewModelTests
    {
        [Fact]
        public void FilterGames_SearchTextMatchesTagsAndNames()
        {
            RunInSta(() =>
            {
                var tempRoot = CreateTempRoot();
                var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

                try
                {
                    using var manager = new GameManager(configPath);
                    using var viewModel = new MainViewModel(manager);

                    var matchingByTag = new Game
                    {
                        Id = "game-tag",
                        Name = "Space Adventure",
                        Tags = new List<string> { "Coop", "Action" }
                    };

                    var matchingByName = new Game
                    {
                        Id = "game-name",
                        Name = "Coop Tactics",
                        Tags = new List<string> { "Strategy" }
                    };

                    var nonMatching = new Game
                    {
                        Id = "game-other",
                        Name = "Builder",
                        Tags = new List<string> { "Sandbox" }
                    };

                    viewModel.SearchText = "coop";

                    Assert.True(InvokeFilterGames(viewModel, matchingByTag));
                    Assert.True(InvokeFilterGames(viewModel, matchingByName));
                    Assert.False(InvokeFilterGames(viewModel, nonMatching));
                }
                finally
                {
                    CleanupTempRoot(tempRoot);
                }
            });
        }

        [Fact]
        public void Constructor_RestoresSavedSortAndTagFilter()
        {
            RunInSta(() =>
            {
                var tempRoot = CreateTempRoot();
                var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

                try
                {
                    using var manager = new GameManager(configPath);
                    manager.Config.UISettings.LibrarySortMode = GameSortMode.PlayTime;
                    manager.Config.UISettings.LibraryFilter = "🏷️ Coop";
                    manager.Config.GameTags["game-1"] = new List<string> { "Coop" };
                    manager.SaveConfigImmediate(manager.Config);

                    using var viewModel = new MainViewModel(manager);

                    Assert.Equal(GameSortMode.PlayTime, viewModel.SelectedSort);
                    Assert.Equal("🏷️ Coop", viewModel.SelectedFilter);

                    var coopGame = new Game
                    {
                        Id = "game-1",
                        Name = "Space Adventure",
                        Tags = new List<string> { "Coop" }
                    };

                    var otherGame = new Game
                    {
                        Id = "game-2",
                        Name = "Builder",
                        Tags = new List<string> { "Sandbox" }
                    };

                    Assert.True(InvokeFilterGames(viewModel, coopGame));
                    Assert.False(InvokeFilterGames(viewModel, otherGame));
                }
                finally
                {
                    CleanupTempRoot(tempRoot);
                }
            });
        }

        [Fact]
        public void SelectedSortAndFilter_ArePersistedToUiSettings()
        {
            var tempRoot = CreateTempRoot();
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");

            try
            {
                RunInSta(() =>
                {
                    using var manager = new GameManager(configPath);
                    using var viewModel = new MainViewModel(manager);

                    viewModel.SelectedSort = GameSortMode.PlayTime;
                    viewModel.SelectedFilter = "Ausgeblendet";
                });

                using var reloadedManager = new GameManager(configPath);

                Assert.Equal(GameSortMode.PlayTime, reloadedManager.Config.UISettings.LibrarySortMode);
                Assert.Equal("Ausgeblendet", reloadedManager.Config.UISettings.LibraryFilter);
            }
            finally
            {
                CleanupTempRoot(tempRoot);
            }
        }

        private static bool InvokeFilterGames(MainViewModel viewModel, Game game)
        {
            var method = typeof(MainViewModel).GetMethod("FilterGames", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("FilterGames-Methode wurde nicht gefunden.");

            return (bool)(method.Invoke(viewModel, new object[] { game })
                ?? throw new InvalidOperationException("FilterGames lieferte kein Ergebnis."));
        }

        private static void RunInSta(Action action)
        {
            Exception? capturedException = null;
            using var finished = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    finished.Set();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            finished.Wait();
            thread.Join();

            if (capturedException != null)
            {
                throw capturedException;
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
