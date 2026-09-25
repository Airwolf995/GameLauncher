using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public sealed class MetadataServiceSteamTests
{
    /// <summary>
    /// Ein Abbruch ist kein Abruffehler. Er muss beim Aufrufer ankommen, damit
    /// dieser ihn als Abbruch behandelt, statt ihn als Fehler zu protokollieren.
    /// </summary>
    [Fact]
    public async Task FetchSteamMetadataAsync_ReichtAbbruchAnDenAufruferWeiter()
    {
        var service = new MetadataService();
        var game = new Game { Id = "steam:620", Name = "Portal 2", Platform = "Steam" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.FetchSteamMetadataAsync(game, cts.Token));
    }
}
