using System.Text.Json;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

/// <summary>
/// Die Kategorien stammen aus echten .item-Manifesten einer Epic-Installation.
/// </summary>
public sealed class EpicScannerTests
{
    [Theory]
    [InlineData("games", "applications")]
    [InlineData("games")]
    public void IsGameManifest_BehaeltSpiele(params string[] categories)
    {
        Assert.True(IsGameManifest(categories));
    }

    /// <summary>
    /// Engine und Plugins zeigen in MainGameAppName auf dieselbe Engine. Da der
    /// Scanner dieses Feld bevorzugt, bekamen alle drei Eintraege die Id
    /// "epic:UE_5.8" - und teilten sich damit Favoriten und Spielzeit.
    /// </summary>
    [Theory]
    [InlineData("engines/ue5", "engines")]
    [InlineData("plugins/engine", "plugins")]
    [InlineData("engines")]
    [InlineData("plugins")]
    public void IsGameManifest_SortiertEngineUndPluginsAus(params string[] categories)
    {
        Assert.False(IsGameManifest(categories));
    }

    /// <summary>
    /// Ohne Kategorien bleibt der Eintrag erhalten: faellt das Feld in einer
    /// kuenftigen Fassung weg, soll lieber kein Spiel verschwinden.
    /// </summary>
    [Fact]
    public void IsGameManifest_BehaeltManifestOhneKategorien()
    {
        using var document = JsonDocument.Parse("""{"DisplayName":"Spiel","AppName":"abc"}""");

        Assert.True(EpicScanner.IsGameManifest(document.RootElement));
    }

    [Fact]
    public void IsGameManifest_BehaeltManifestMitUnerwartetemKategorienfeld()
    {
        using var document = JsonDocument.Parse("""{"AppCategories":"games"}""");

        Assert.True(EpicScanner.IsGameManifest(document.RootElement));
    }

    private static bool IsGameManifest(string[] categories)
    {
        string json = JsonSerializer.Serialize(new { AppCategories = categories });
        using var document = JsonDocument.Parse(json);
        return EpicScanner.IsGameManifest(document.RootElement);
    }
}
