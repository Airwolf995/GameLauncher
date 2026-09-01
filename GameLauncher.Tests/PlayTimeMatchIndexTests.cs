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
        public void TryMatchProcess_DisambiguatesDuplicateExecutableNamesByPath()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", ExecutableName = "game.exe", InstallDirectory = @"C:\Games\One", IsManual = false },
                new() { Id = "g2", Name = "Game 2", ExecutableName = "game.exe", InstallDirectory = @"C:\Games\Two", IsManual = false }
            };

            index.Rebuild(games);

            Assert.False(index.TryMatchProcessByName("game", out _));
            Assert.True(index.TryMatchProcess("game", @"C:\Games\Two\game.exe", out var gameId));
            Assert.Equal("g2", gameId);
        }

        [Fact]
        public void TryMatchProcessByName_MatchedExecutableNameIsIndependentOfExeSuffix()
        {
            var index = new PlayTimeMatchIndex();
            index.Rebuild(
            [
                new Game { Id = "g1", Name = "Game 1", ExecutableName = "game", IsManual = false }
            ]);

            bool matched = index.TryMatchProcessByName("game.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("g1", gameId);
        }

        [Fact]
        public void TryMatchProcess_OrdnetManuellemSpielMitProgrammpfadZu()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "manual1",
                    Name = "Manuelles Spiel",
                    ExecutableName = "manual.exe",
                    LaunchType = "exe",
                    IsManual = true
                }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("manual.exe", @"C:\Manual\manual.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("manual1", gameId);
        }

        /// <summary>
        /// Startet ein manueller Eintrag ein Startprogramm, das sich beendet und das
        /// eigentliche Spiel zurücklässt, greift die Zuordnung über das
        /// Installationsverzeichnis.
        /// </summary>
        [Fact]
        public void TryMatchProcess_OrdnetManuellemSpielUeberDasVerzeichnisZu()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "manual2",
                    Name = "Manuelles Spiel",
                    Path = @"C:\Spiele\Beispiel\start-launcher.exe",
                    InstallDirectory = @"C:\Spiele\Beispiel",
                    LaunchType = "exe",
                    IsManual = true
                }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("spiel", @"C:\Spiele\Beispiel\spiel.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("manual2", gameId);
        }

        [Fact]
        public void TryMatchProcess_UebergehtManuellesSpielOhneZuordenbarenProzess()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "manual3",
                    Name = "Manueller Eintrag",
                    Path = "battlenet://WoW",
                    InstallDirectory = "battlenet:",
                    LaunchType = "uri",
                    IsManual = true
                }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcess("wow", @"C:\Spiele\WoW\wow.exe", out _);

            Assert.False(matched);
        }

        /// <summary>
        /// Ein importierter Eintrag, der über seinen Store-Client startet, hat als
        /// umgebendes Verzeichnis den Ordner des Clients. Würde er beobachtet, zählte
        /// jeder Prozess des dauerhaft laufenden Launchers auf das Spiel.
        /// </summary>
        [Fact]
        public void TryMatchProcess_BeobachtetDenOrdnerEinesLaunchersNicht()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "manual_launcher",
                    Name = "Diablo IV",
                    Path = @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
                    ExecutableName = "Diablo IV.exe",
                    InstallDirectory = @"C:\Program Files (x86)\Battle.net",
                    LaunchType = "exe",
                    IsManual = true
                }
            };

            index.Rebuild(games);

            Assert.False(index.TryMatchProcess(
                "Battle.net", @"C:\Program Files (x86)\Battle.net\Battle.net.exe", out _));
            Assert.False(index.TryMatchProcess(
                "Agent", @"C:\Program Files (x86)\Battle.net\Agent\Agent.exe", out _));
        }

        /// <summary>
        /// Der hinterlegte Prozessname bleibt der Weg, ein solches Spiel doch zu
        /// erfassen - genau das sagt auch <see cref="Game.SupportsPlayTimeTracking"/> zu.
        /// </summary>
        [Fact]
        public void TryMatchProcess_ErfasstLauncherEintragUeberHinterlegtenProzessnamen()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "manual_launcher",
                    Name = "Diablo IV",
                    Path = @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
                    ExecutableName = "Diablo IV.exe",
                    InstallDirectory = @"C:\Program Files (x86)\Battle.net",
                    LaunchType = "exe",
                    IsManual = true
                }
            };

            index.Rebuild(games);

            Assert.True(index.TryMatchProcess("Diablo IV", @"D:\Diablo IV\Diablo IV.exe", out var gameId));
            Assert.Equal("manual_launcher", gameId);
        }
    }
}
