using GameLauncher.Services.Localization;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class EaScannerTests
{
    [Fact]
    public void BuildLaunchUri_EscapedDieOfferIdUndVerwendetDasEaProtokoll()
    {
        string uri = EaScanner.BuildLaunchUri("OFFER ID&1");

        Assert.Equal("origin2://game/launch?offerIds=OFFER%20ID%261", uri);
    }

    [Fact]
    public void BuildLaunchUri_VerwendetDieInhaltskennungAusDenInstallationsdaten()
    {
        Assert.Equal("origin2://game/launch?offerIds=1026023", EaScanner.BuildLaunchUri("1026023"));
    }
}

/// <summary>
/// Die Beispielinhalte bilden die beiden Aufbauten nach, die in echten
/// installerdata.xml-Dateien vorkommen.
/// </summary>
public sealed class EaGameManifestReaderTests
{
    private const string NeueresManifest = """
        <?xml version='1.0' encoding='utf-8'?>
        <DiPManifest version="4.0">
          <contentIDs>
            <contentID>1026023</contentID>
          </contentIDs>
          <gameTitles>
            <gameTitle locale="en_US">Battlefield 1</gameTitle>
            <gameTitle locale="de_DE">Battlefield 1 Deutsch</gameTitle>
          </gameTitles>
        </DiPManifest>
        """;

    private const string AelteresManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <game gameVersion="1.5.0.0" manifestVersion="2.1">
          <contentIDs>
            <contentID>71628</contentID>
            <contentID>71530</contentID>
          </contentIDs>
          <metadata>
            <localeInfo locale="en_US">
              <title>Need for Speed Most Wanted</title>
            </localeInfo>
            <localeInfo locale="de_DE">
              <title>Need for Speed Most Wanted Deutsch</title>
            </localeInfo>
          </metadata>
        </game>
        """;

    [Fact]
    public void Parse_LiestKennungUndTitelAusDemNeuerenAufbau()
    {
        var manifest = EaGameManifestReader.Parse(NeueresManifest, AppLanguage.German);

        Assert.NotNull(manifest);
        Assert.Equal("1026023", manifest.ContentId);
        Assert.Equal("Battlefield 1 Deutsch", manifest.Title);
    }

    [Fact]
    public void Parse_LiestKennungUndTitelAusDemAelterenAufbau()
    {
        var manifest = EaGameManifestReader.Parse(AelteresManifest, AppLanguage.German);

        Assert.NotNull(manifest);
        Assert.Equal("71628", manifest.ContentId);
        Assert.Equal("Need for Speed Most Wanted Deutsch", manifest.Title);
    }

    /// <summary>
    /// Die erste Kennung bezeichnet das Spiel, die weiteren gehören zu Zusatzinhalten.
    /// </summary>
    [Fact]
    public void Parse_VerwendetDieErsteKennung()
    {
        var manifest = EaGameManifestReader.Parse(AelteresManifest, AppLanguage.English);

        Assert.Equal("71628", manifest!.ContentId);
    }

    [Fact]
    public void Parse_VerwendetDieEingestellteSprache()
    {
        var manifest = EaGameManifestReader.Parse(NeueresManifest, AppLanguage.English);

        Assert.Equal("Battlefield 1", manifest!.Title);
    }

    [Fact]
    public void Parse_FaelltAufEineVorhandeneSpracheZurueck()
    {
        const string nurFranzoesisch = """
            <DiPManifest version="4.0">
              <contentIDs><contentID>555</contentID></contentIDs>
              <gameTitles><gameTitle locale="fr_FR">Un Jeu</gameTitle></gameTitles>
            </DiPManifest>
            """;

        var manifest = EaGameManifestReader.Parse(nurFranzoesisch, AppLanguage.German);

        Assert.Equal("Un Jeu", manifest!.Title);
    }

    [Fact]
    public void Parse_LiefertOhneTitelNurDieKennung()
    {
        const string ohneTitel = """
            <DiPManifest version="4.0">
              <contentIDs><contentID>999</contentID></contentIDs>
            </DiPManifest>
            """;

        var manifest = EaGameManifestReader.Parse(ohneTitel, AppLanguage.German);

        Assert.NotNull(manifest);
        Assert.Equal("999", manifest.ContentId);
        Assert.Null(manifest.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kein xml")]
    [InlineData("<DiPManifest version=\"4.0\"></DiPManifest>")]
    public void Parse_LiefertOhneVerwertbareKennungKeinManifest(string xml)
    {
        Assert.Null(EaGameManifestReader.Parse(xml, AppLanguage.German));
    }
}
