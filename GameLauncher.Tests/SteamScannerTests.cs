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
}
