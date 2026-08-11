using GameLauncher.Models;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class GameLibraryLoaderTests
{
    [Fact]
    public async Task ScanPlatformAsync_SetztNachFehlerBeimErzeugenMitAnderenScannernFort()
    {
        List<Game> games = await GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () => throw new InvalidOperationException("Ungültige Scanner-Konfiguration"),
            CancellationToken.None);

        Assert.Empty(games);
    }

    [Fact]
    public async Task ScanPlatformAsync_GibtErgebnisEinesFunktionierendenScannersZurück()
    {
        var expectedGame = new Game { Id = "test:1", Name = "Testspiel" };

        List<Game> games = await GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () => new TestScanner([expectedGame]),
            CancellationToken.None);

        Assert.Equal([expectedGame], games);
    }

    private sealed class TestScanner(List<Game> games) : IPlatformScanner
    {
        public string PlatformName => "Testplattform";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default) => Task.FromResult(games);
    }
}
