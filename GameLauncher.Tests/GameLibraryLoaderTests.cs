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

    /// <summary>
    /// Die Scanner-Konstruktoren erkennen ohne eingetragene Pfade selbst die
    /// Bibliotheken; beim Xbox-Scanner dauerte das gemessen 570-750 ms. Beim
    /// Start ist der Aufrufer der Oberflächen-Thread, er darf darauf nicht warten.
    /// </summary>
    [Fact]
    public async Task ScanPlatformAsync_ErzeugtDenScannerNichtAufDemAufrufendenThread()
    {
        using var scannerCreationReleased = new ManualResetEventSlim();
        bool scannerCreated = false;

        Task<LibraryScanResult> scanTask = GameLibraryLoader.ScanPlatformAsync(
            "Testplattform",
            () =>
            {
                scannerCreationReleased.Wait(TimeSpan.FromSeconds(5));
                Volatile.Write(ref scannerCreated, true);
                return new TestScanner([]);
            },
            CancellationToken.None);

        bool returnedBeforeScannerCreated = !Volatile.Read(ref scannerCreated);
        scannerCreationReleased.Set();
        await scanTask;

        Assert.True(returnedBeforeScannerCreated);
    }

    private sealed class TestScanner(List<Game> games) : IPlatformScanner
    {
        public string PlatformName => "Testplattform";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default) => Task.FromResult(games);
    }
}
