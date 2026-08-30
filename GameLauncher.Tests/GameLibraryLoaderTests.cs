using GameLauncher.Models;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class GameLibraryLoaderTests
{
    [Fact]
    public async Task ScanPlatformAsync_SetztNachFehlerBeimErzeugenMitAnderenScannernFort()
    {
        LibraryScanResult result = await GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () => throw new InvalidOperationException("Ungültige Scanner-Konfiguration"),
            CancellationToken.None);

        Assert.Empty(result.Games);
    }

    [Fact]
    public async Task ScanPlatformAsync_MeldetDieFehlgeschlagenePlattform()
    {
        LibraryScanResult result = await GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () => throw new InvalidOperationException("Ungültige Scanner-Konfiguration"),
            CancellationToken.None);

        Assert.Equal(["Testplattform"], result.FailedPlatforms);
    }

    [Fact]
    public async Task ScanPlatformAsync_GibtErgebnisEinesFunktionierendenScannersZurück()
    {
        var expectedGame = new Game { Id = "test:1", Name = "Testspiel" };

        LibraryScanResult result = await GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () => new TestScanner([expectedGame]),
            CancellationToken.None);

        Assert.Equal([expectedGame], result.Games);
        Assert.Empty(result.FailedPlatforms);
    }

    private sealed class TestScanner(List<Game> games) : IPlatformScanner
    {
        public string PlatformName => "Testplattform";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default) => Task.FromResult(games);
    }
}
