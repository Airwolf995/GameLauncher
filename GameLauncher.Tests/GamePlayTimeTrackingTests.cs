using GameLauncher.Models;

namespace GameLauncher.Tests;

public sealed class GamePlayTimeTrackingTests
{
    [Theory]
    [InlineData("exe")]
    [InlineData("uri")]
    public void SupportsPlayTimeTracking_GiltFuerSpieleDerPlattformenImmer(string launchType)
    {
        var game = new Game { Id = "steam:620", Name = "Portal 2", LaunchType = launchType, IsManual = false };

        Assert.True(game.SupportsPlayTimeTracking);
    }

    [Fact]
    public void SupportsPlayTimeTracking_GiltFuerManuelleSpieleMitProgrammpfad()
    {
        var game = new Game { Id = "manual_1", Name = "Spiel", LaunchType = "exe", IsManual = true };

        Assert.True(game.SupportsPlayTimeTracking);
    }

    [Fact]
    public void SupportsPlayTimeTracking_GiltNichtFuerManuelleEintraegeOhneProzess()
    {
        var game = new Game { Id = "manual_2", Name = "Eintrag", LaunchType = "uri", IsManual = true };

        Assert.False(game.SupportsPlayTimeTracking);
    }

    /// <summary>
    /// Der Verknüpfungsimport übernimmt Spiele, die über ihren Store-Client
    /// starten, etwa Battle.net-Titel. Deren Programmpfad ist der Client selbst;
    /// gemessen würde sonst die Laufzeit des im Hintergrund liegenden Launchers.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe")]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe")]
    [InlineData(@"C:\Riot Games\Riot Client\RiotClientServices.exe")]
    public void SupportsPlayTimeTracking_GiltNichtFuerManuelleEintraegeAufEinenLauncher(string path)
    {
        var game = new Game
        {
            Id = "manual_3",
            Name = "Diablo IV",
            LaunchType = "exe",
            IsManual = true,
            Path = path
        };

        Assert.False(game.SupportsPlayTimeTracking);
    }

    [Fact]
    public void SupportsPlayTimeTracking_GiltFuerLauncherEintragMitHinterlegtemProzessnamen()
    {
        var game = new Game
        {
            Id = "manual_4",
            Name = "Diablo IV",
            LaunchType = "exe",
            IsManual = true,
            Path = @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
            ExecutableName = "Diablo IV.exe"
        };

        Assert.True(game.SupportsPlayTimeTracking);
    }

    [Fact]
    public void SupportsPlayTimeTracking_GiltFuerManuelleSpieleMitEigenerProgrammdatei()
    {
        var game = new Game
        {
            Id = "manual_5",
            Name = "Spiel",
            LaunchType = "exe",
            IsManual = true,
            Path = @"D:\Spiele\Frostpunk\Frostpunk.exe"
        };

        Assert.True(game.SupportsPlayTimeTracking);
    }
}
