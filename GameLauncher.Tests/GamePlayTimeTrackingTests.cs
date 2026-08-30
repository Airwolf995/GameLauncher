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
}
