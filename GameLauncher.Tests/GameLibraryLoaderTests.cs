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

    [Fact]
    public async Task ScanPlatformAsync_BrichtBeiZeitlimitDenScannerAbUndMeldetDiePlattform()
    {
        using var callerCancellation = new CancellationTokenSource();
        var scanner = new WaitingScanner(observeCancellation: true);
        Task<LibraryScanResult> scan = GameLibraryLoader.ScanPlatformAsync(
            "Langsame Plattform", () => scanner, callerCancellation.Token, TimeSpan.FromSeconds(1));

        try
        {
            await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            LibraryScanResult result = await scan.WaitAsync(TimeSpan.FromSeconds(5));
            await scanner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(scanner.Token.IsCancellationRequested);
            Assert.False(callerCancellation.IsCancellationRequested);
            Assert.Empty(result.Games);
            Assert.Equal(["Langsame Plattform"], result.FailedPlatforms);
        }
        finally
        {
            scanner.Release.TrySetResult();
            await scan;
        }
    }

    [Fact]
    public async Task ScanPlatformAsync_WartetBeiZeitlimitNichtAufEinenBlockierendenScanner()
    {
        var scanner = new WaitingScanner(observeCancellation: false);
        Task<LibraryScanResult> scan = GameLibraryLoader.ScanPlatformAsync(
            "Blockierte Plattform", () => scanner, CancellationToken.None, TimeSpan.FromSeconds(1));

        try
        {
            await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            LibraryScanResult result = await scan.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(scanner.Token.IsCancellationRequested);
            Assert.False(scanner.Completed.Task.IsCompleted);
            Assert.Equal(["Blockierte Plattform"], result.FailedPlatforms);
        }
        finally
        {
            scanner.Release.TrySetResult();
            await scanner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scan;
        }
    }

    [Fact]
    public async Task ScanPlatformAsync_ReichtDenAbbruchDesAufrufersAnDenScannerWeiter()
    {
        using var callerCancellation = new CancellationTokenSource();
        var scanner = new WaitingScanner(observeCancellation: true);
        Task<LibraryScanResult> scan = GameLibraryLoader.ScanPlatformAsync(
            "Testplattform", () => scanner, callerCancellation.Token);

        try
        {
            await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            callerCancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await scan.WaitAsync(TimeSpan.FromSeconds(5)));
            await scanner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scanner.Token.IsCancellationRequested);
        }
        finally
        {
            scanner.Release.TrySetResult();
            await scanner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class WaitingScanner(bool observeCancellation) : IPlatformScanner
    {
        public string PlatformName => "Testplattform";
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            Token = ct;
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(observeCancellation ? ct : CancellationToken.None);
                return [];
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class TestScanner(List<Game> games) : IPlatformScanner
    {
        public string PlatformName => "Testplattform";

        public Task<List<Game>> ScanAsync(CancellationToken ct = default) => Task.FromResult(games);
    }
}
