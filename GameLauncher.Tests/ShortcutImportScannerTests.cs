using GameLauncher.Models;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class ShortcutImportScannerTests
{
    [Theory]
    [InlineData(@"C:\Games\Diablo IV\Diablo IV.exe")]
    [InlineData(@"D:\Rockstar Games\GTA V\PlayGTAV.exe")]
    public void IsLikelyGameTarget_AkzeptiertSpielprogramme(string targetPath)
    {
        Assert.True(ShortcutImportScanner.IsLikelyGameTarget(targetPath));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"C:\Program Files\Battle.net\Battle.net.exe")]
    [InlineData(@"C:\Program Files\GOG Galaxy\GalaxyClient.exe")]
    public void IsLikelyGameTarget_LehntLauncherAb(string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsLikelyGameTarget(targetPath));
    }

    [Theory]
    [InlineData(@"C:\Games\Spiel\unins000.exe")]
    [InlineData(@"C:\Games\Spiel\CrashReporter.exe")]
    [InlineData(@"C:\Games\Spiel\SpielConfig.exe")]
    public void IsLikelyGameTarget_LehntHilfsprogrammeAb(string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsLikelyGameTarget(targetPath));
    }

    [Theory]
    [InlineData(@"C:\Games\Spiel\handbuch.pdf")]
    [InlineData(@"C:\Games\Spiel\start.bat")]
    [InlineData("")]
    public void IsLikelyGameTarget_LehntNichtAusfuehrbareZieleAb(string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsLikelyGameTarget(targetPath));
    }

    [Fact]
    public void IsLikelyGameTarget_LehntProgrammeAusDemWindowsVerzeichnisAb()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string targetPath = Path.Combine(windowsDirectory, "System32", "notepad.exe");

        Assert.False(ShortcutImportScanner.IsLikelyGameTarget(targetPath));
    }

    [Fact]
    public void IsAlreadyKnown_ErkenntGleichenNamen()
    {
        var candidate = new ShortcutGameCandidate("Diablo IV", @"C:\Games\D4\game.exe", "", "");
        var existing = new[] { new Game { Id = "manual_1", Name = "diablo iv" } };

        Assert.True(ShortcutImportScanner.IsAlreadyKnown(candidate, existing));
    }

    [Fact]
    public void IsAlreadyKnown_ErkenntSpielImBekanntenInstallationsverzeichnis()
    {
        var candidate = new ShortcutGameCandidate(
            "Ein Spiel",
            @"C:\Games\Portal 2\bin\portal2.exe",
            "",
            "");
        var existing = new[]
        {
            new Game { Id = "steam:620", Name = "Portal 2", InstallDirectory = @"C:\Games\Portal 2" }
        };

        Assert.True(ShortcutImportScanner.IsAlreadyKnown(candidate, existing));
    }

    [Fact]
    public void IsAlreadyKnown_MeldetUnbekannteSpieleNichtAlsVorhanden()
    {
        var candidate = new ShortcutGameCandidate("Neues Spiel", @"D:\Neu\neu.exe", "", "");
        var existing = new[]
        {
            new Game { Id = "steam:620", Name = "Portal 2", InstallDirectory = @"C:\Games\Portal 2" }
        };

        Assert.False(ShortcutImportScanner.IsAlreadyKnown(candidate, existing));
    }
}
