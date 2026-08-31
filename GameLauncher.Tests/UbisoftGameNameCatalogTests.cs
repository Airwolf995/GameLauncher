using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

/// <summary>
/// Die Beispielinhalte bilden die Muster nach, die in einer echten
/// configurations-Datei von Ubisoft Connect vorkommen.
/// </summary>
public sealed class UbisoftGameNameCatalogTests
{
    [Fact]
    public void Parse_BevorzugtDenAnzeigenamen()
    {
        const string content = """
        version: 2.0
        root:
          name: Watch_Dogs
          display_name: Watch Dogs
          start_game:
            online:
              executables:
              - working_directory:
                  register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\274\InstallDir
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal("Watch Dogs", names["274"]);
    }

    [Fact]
    public void Parse_VerwendetDenNamenOhneAnzeigenamen()
    {
        const string content = """
        version: 2.0
        root:
          name: Riders Republic
          start_game:
            online:
              executables:
              - working_directory:
                  register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\5487\InstallDir
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal("Riders Republic", names["5487"]);
    }

    [Fact]
    public void Parse_LoestLokalisierungsschluesselAusDemStandardabschnittAuf()
    {
        const string content = """
        version: 2.0
        root:
          name: l1
          start_game:
            online:
              executables:
              - working_directory:
                  register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\3539\InstallDir
        localizations:
          default:
            l1: Assassin's Creed Origins
            l2: bild.jpg
          zh-CN:
            l1: Anderer Titel
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal("Assassin's Creed Origins", names["3539"]);
    }

    [Fact]
    public void Parse_EntferntUmschliessendeAnfuehrungszeichen()
    {
        const string content = """
        version: 2.0
        root:
          name: 'South Park: The Fractured But Whole'
          start_game:
            online:
              executables:
              - working_directory:
                  register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\3088\InstallDir
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal("South Park: The Fractured But Whole", names["3088"]);
    }

    [Fact]
    public void Parse_EntferntMarkensymboleAusDemTitel()
    {
        const string content = """
        version: 2.0
        root:
          name: Far Cry® 6
          register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\5266\InstallDir
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal("Far Cry 6", names["5266"]);
    }

    [Fact]
    public void Parse_TrenntMehrereEintraegeAnDerVersionszeile()
    {
        const string content = """
        version: 2.0
        root:
          display_name: Erstes Spiel
          register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\111\InstallDir
        version: 2.0
        root:
          display_name: Zweites Spiel
          register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\222\InstallDir
        """;

        var names = UbisoftGameNameCatalog.Parse(content);

        Assert.Equal(2, names.Count);
        Assert.Equal("Erstes Spiel", names["111"]);
        Assert.Equal("Zweites Spiel", names["222"]);
    }

    [Fact]
    public void Parse_UebergehtEintraegeOhneInstallationskennung()
    {
        const string content = """
        version: 2.0
        root:
          name: Ein Zusatzinhalt
          description: DESCR
        """;

        Assert.Empty(UbisoftGameNameCatalog.Parse(content));
    }

    [Fact]
    public void Parse_UebergehtUnaufloesbareLokalisierungsschluessel()
    {
        const string content = """
        version: 2.0
        root:
          name: l1
          register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\999\InstallDir
        localizations: {}
        """;

        Assert.Empty(UbisoftGameNameCatalog.Parse(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kein gueltiger Inhalt")]
    public void Parse_LiefertBeiUnbrauchbaremInhaltEineLeereZuordnung(string content)
    {
        Assert.Empty(UbisoftGameNameCatalog.Parse(content));
    }
}
