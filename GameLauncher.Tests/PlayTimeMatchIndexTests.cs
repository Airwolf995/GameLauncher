using System.Collections.Generic;
using GameLauncher.Models;
using GameLauncher.Services;
using Xunit;

namespace GameLauncher.Tests
{
    public class PlayTimeMatchIndexTests
    {
        [Fact]
        public void TryMatchProcessByName_FindetSpielUeberHinterlegtenProgrammnamen()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", ExecutableName = "doom.exe", IsManual = false },
                new() { Id = "g2", Name = "Game 2", InstallDirectory = @"C:\Games\Doom", IsManual = false }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcessByName("doom.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("g1", gameId);
        }

        [Fact]
        public void TryMatchProcessByPath_UsesInstallDirectoryPrefix()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", InstallDirectory = @"C:\Games\MyGame", IsManual = false }
            };

            index.Rebuild(games);

            var matched = index.TryMatchProcessByPath(@"C:\Games\MyGame\bin\mygame.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("g1", gameId);
        }

        [Fact]
        public void TryMatchProcessByPath_DisambiguatesDuplicateExecutableNames()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new() { Id = "g1", Name = "Game 1", ExecutableName = "game.exe", InstallDirectory = @"C:\Games\One", IsManual = false },
                new() { Id = "g2", Name = "Game 2", ExecutableName = "game.exe", InstallDirectory = @"C:\Games\Two", IsManual = false }
            };

            index.Rebuild(games);

            Assert.False(index.TryMatchProcessByName("game", out _));
            Assert.True(index.TryMatchProcessByPath(@"C:\Games\Two\game.exe", out var gameId));
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
        public void TryMatchProcessByName_OrdnetManuellemSpielMitProgrammnamenZu()
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

            var matched = index.TryMatchProcessByName("manual.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("manual1", gameId);
        }

        /// <summary>
        /// Startet ein manueller Eintrag ein Startprogramm, das sich beendet und das
        /// eigentliche Spiel zurücklässt, greift die Zuordnung über das
        /// Installationsverzeichnis.
        /// </summary>
        [Fact]
        public void TryMatchProcessByPath_OrdnetManuellemSpielUeberDasVerzeichnisZu()
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

            var matched = index.TryMatchProcessByPath(@"C:\Spiele\Beispiel\spiel.exe", out var gameId);

            Assert.True(matched);
            Assert.Equal("manual2", gameId);
        }

        [Fact]
        public void Rebuild_UebergehtManuellesSpielOhneZuordenbarenProzess()
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

            Assert.False(index.TryMatchProcessByName("wow", out _));
            Assert.False(index.TryMatchProcessByPath(@"C:\Spiele\WoW\wow.exe", out _));
        }

        /// <summary>
        /// Ein importierter Eintrag, der über seinen Store-Client startet, hat als
        /// umgebendes Verzeichnis den Ordner des Clients. Würde er beobachtet, zählte
        /// jeder Prozess des dauerhaft laufenden Launchers auf das Spiel.
        /// </summary>
        [Fact]
        public void Rebuild_BeobachtetDenOrdnerEinesLaunchersNicht()
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

            Assert.False(index.TryMatchProcessByName("Battle.net", out _));
            Assert.False(index.TryMatchProcessByName("Agent", out _));
            Assert.False(index.TryMatchProcessByPath(
                @"C:\Program Files (x86)\Battle.net\Battle.net.exe", out _));
            Assert.False(index.TryMatchProcessByPath(
                @"C:\Program Files (x86)\Battle.net\Agent\Agent.exe", out _));
        }

        /// <summary>
        /// Der hinterlegte Prozessname bleibt der Weg, ein solches Spiel doch zu
        /// erfassen - genau das sagt auch <see cref="Game.SupportsPlayTimeTracking"/> zu.
        /// </summary>
        [Fact]
        public void TryMatchProcessByName_ErfasstLauncherEintragUeberHinterlegtenProzessnamen()
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

            Assert.True(index.TryMatchProcessByName("Diablo IV", out var gameId));
            Assert.Equal("manual_launcher", gameId);
        }

        /// <summary>
        /// Ein gescanntes Battle.net-Spiel startet über den Client, liegt aber in
        /// einem eigenen Ordner. Erfasst wird das Spiel dort, nicht der Client.
        /// </summary>
        [Fact]
        public void TryMatchProcessByPath_ErfasstGescanntesLauncherSpielUeberDenSpielordner()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "bnet_zeus",
                    Name = "Call of Duty Black Ops Cold War",
                    Path = @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
                    Args = "--exec=\"launch_uid zeus\"",
                    InstallDirectory = @"D:\Battle.net\Call of Duty Black Ops Cold War",
                    LaunchType = "exe",
                    IsManual = false
                }
            };

            index.Rebuild(games);

            Assert.True(index.TryMatchProcessByPath(
                @"D:\Battle.net\Call of Duty Black Ops Cold War\BlackOpsColdWar.exe", out var gameId));
            Assert.Equal("bnet_zeus", gameId);
            Assert.False(index.TryMatchProcessByPath(
                @"C:\Program Files (x86)\Battle.net\Battle.net.exe", out _));
            Assert.False(index.TryMatchProcessByName("Battle.net", out _));
        }

        /// <summary>
        /// Der Programmpfad eines manuellen Eintrags ist Benutzereingabe. Auch
        /// ungültige Zeichen dürfen den Aufbau des Index nicht abbrechen, sonst
        /// fiele die Spielzeiterfassung für alle Spiele aus.
        /// </summary>
        [Fact]
        public void Rebuild_ToleriertUngueltigeZeichenImProgrammpfad()
        {
            var index = new PlayTimeMatchIndex();
            var games = new List<Game>
            {
                new()
                {
                    Id = "kaputt",
                    Name = "Kaputter Pfad",
                    Path = "C:\\Spiele\\<ungültig>|\"\0\\spiel.exe",
                    LaunchType = "exe",
                    IsManual = true
                },
                new() { Id = "g1", Name = "Game 1", ExecutableName = "game.exe", IsManual = false }
            };

            index.Rebuild(games);

            Assert.True(index.TryMatchProcessByName("game", out var gameId));
            Assert.Equal("g1", gameId);
            Assert.True(index.TryMatchProcessByName("spiel", out gameId));
            Assert.Equal("kaputt", gameId);
        }
    }
}
