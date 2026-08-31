using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class SteamScannerTests
{
    [Fact]
    public void TryReadManifestValue_LiestMaskierteAnführungszeichenVollständig()
    {
        const string manifest = "\"AppState\"\n{\n  \"name\" \"Spiel mit \\\"Zitat\\\"\"\n}";

        string? name = SteamScanner.TryReadManifestValue(manifest, "name");

        Assert.Equal("Spiel mit \"Zitat\"", name);
    }

    [Fact]
    public void TryReadManifestValue_LiestDoppeltePfadtrennerAlsEinzelnenTrenner()
    {
        const string manifest = "\"installdir\" \"Mein\\\\Spiel\"";

        string? installDirectory = SteamScanner.TryReadManifestValue(manifest, "installdir");

        Assert.Equal("Mein\\Spiel", installDirectory);
    }

    [Theory]
    [InlineData(4)]    // vollständig installiert
    [InlineData(6)]    // installiert, Aktualisierung ausstehend
    [InlineData(1028)] // installiert, zusätzliche Statusbits gesetzt
    public void IsFullyInstalled_ErkenntGesetztesInstallationsBit(int stateFlags)
    {
        string manifest = $"\"AppState\"\n{{\n  \"StateFlags\" \"{stateFlags}\"\n}}";

        Assert.True(SteamScanner.IsFullyInstalled(manifest));
    }

    [Theory]
    [InlineData(2)]    // Aktualisierung erforderlich, nicht spielbar
    [InlineData(1026)] // Installation läuft noch
    public void IsFullyInstalled_ErkenntUnvollständigeInstallationen(int stateFlags)
    {
        string manifest = $"\"AppState\"\n{{\n  \"StateFlags\" \"{stateFlags}\"\n}}";

        Assert.False(SteamScanner.IsFullyInstalled(manifest));
    }

    [Theory]
    [InlineData("\"AppState\"\n{\n  \"name\" \"Spiel\"\n}")]
    [InlineData("\"AppState\"\n{\n  \"StateFlags\" \"unbekannt\"\n}")]
    public void IsFullyInstalled_BehältSpieleOhneVerwertbarenStatus(string manifest)
    {
        Assert.True(SteamScanner.IsFullyInstalled(manifest));
    }
}
