using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

/// <summary>
/// Die Deinstallationsaufrufe stammen aus einer echten Installation von
/// Call of Duty: Black Ops Cold War und dem Battle.net-Client.
/// </summary>
public sealed class BattleNetScannerTests
{
    private const string ClientPath = @"C:\Program Files (x86)\Battle.net\Battle.net.exe";

    [Theory]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE --uid=zeus --displayname=\"Call of Duty Black Ops Cold War\"", "zeus")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE --uid=battle.net --displayname=\"Battle.net\"", "battle.net")]
    public void TryReadUid_LiestDieProduktkennung(string uninstallString, string expectedUid)
    {
        Assert.Equal(expectedUid, BattleNetScanner.TryReadUid(uninstallString));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"C:\\Program Files\\Ubisoft\\uninstall.exe\" --uid=abc")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=deDE")]
    public void TryReadUid_LiefertNullFuerFremdeOderUnvollstaendigeEintraege(string? uninstallString)
    {
        Assert.Null(BattleNetScanner.TryReadUid(uninstallString));
    }

    [Fact]
    public void CreateGame_OeffnetDasSpielUeberDenClientMitDerProduktkennung()
    {
        var entry = new BattleNetScanner.UninstallEntry(
            "zeus",
            "Call of Duty Black Ops Cold War",
            @"D:\Battle.net\Call of Duty Black Ops Cold War",
            @"D:\Battle.net\Call of Duty Black Ops Cold War\BlackOpsColdWar.exe");

        var game = BattleNetScanner.CreateGame(entry, ClientPath);

        Assert.Equal("bnet_zeus", game.Id);
        Assert.Equal("Call of Duty Black Ops Cold War", game.Name);
        Assert.Equal(ClientPath, game.Path);
        Assert.Equal("--exec=\"launch_uid zeus\"", game.Args);
        Assert.Equal("exe", game.LaunchType);
        Assert.Equal(Constants.Platforms.BattleNet, game.Platform);
        Assert.Equal(@"D:\Battle.net\Call of Duty Black Ops Cold War", game.InstallDirectory);
        Assert.False(game.IsManual);
    }

    [Fact]
    public void CreateGame_NimmtOhneAnzeigenamenDenOrdnernamen()
    {
        var entry = new BattleNetScanner.UninstallEntry("fenris", " ", @"D:\Battle.net\Diablo IV", "");

        var game = BattleNetScanner.CreateGame(entry, ClientPath);

        Assert.Equal("Diablo IV", game.Name);
    }
}
