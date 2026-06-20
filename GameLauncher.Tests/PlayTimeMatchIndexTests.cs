using System.Collections.Generic;
using GameLauncher.Models;
using GameLauncher.Services;
using Xunit;

namespace GameLauncher.Tests
{
    public class PlayTimeMatchIndexTests
    {
        [Fact]
        public void TryMatchProcess_UsesExecutableNameLookup()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", ExecutableName = "doom.exe", IsManual = false },
                new() { Id = "g2", Name = "Game 2", InstallDirectory = @"C:\Games\Doom", IsManual = false }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("doom.exe", @"C:\Games\Doom\doom.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("g1", gameId);
        }

        [Fact]
        public void TryMatchProcess_UsesInstallDirectoryPrefix()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", InstallDirectory = @"C:\Games\MyGame", IsManual = false }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("mygame.exe", @"C:\Games\MyGame\bin\mygame.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("g1", gameId);
        }

        [Fact]
        public void TryMatchProcess_SkipsManualGames()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "manual1", Name = "Manual Game", ExecutableName = "manual.exe", IsManual = true }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("manual.exe", @"C:\Manual\manual.exe", out _);

            Assert.False(matched);
        }
    }
}
