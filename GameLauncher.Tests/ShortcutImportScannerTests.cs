using GameLauncher.Models;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class ShortcutImportScannerTests
{
    [Theory]
    [InlineData(@"C:\Games\Diablo IV\Diablo IV.exe")]
    [InlineData(@"D:\Rockstar Games\GTA V\PlayGTAV.exe")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    public void IsSupportedTarget_AkzeptiertStartbareProgramme(string targetPath)
    {
        Assert.True(ShortcutImportScanner.IsSupportedTarget(targetPath));
    }

    [Theory]
    [InlineData(@"C:\Games\Spiel\handbuch.pdf")]
    [InlineData(@"C:\Games\Spiel\start.bat")]
    [InlineData(@"C:\Windows\System32\computer.msc")]
    [InlineData("")]
    public void IsSupportedTarget_LehntNichtAusfuehrbareZieleAb(string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsSupportedTarget(targetPath));
    }

    [Fact]
    public void IsSupportedTarget_LehntProgrammeAusDemWindowsVerzeichnisAb()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string targetPath = Path.Combine(windowsDirectory, "System32", "notepad.exe");

        Assert.False(ShortcutImportScanner.IsSupportedTarget(targetPath));
    }

    [Theory]
    [InlineData("Diablo IV", @"C:\Games\Diablo IV\Diablo IV.exe")]
    [InlineData("Ghostrunner DX12", @"D:\GOG\Ghostrunner\Ghostrunner-Win64-Shipping.exe")]
    public void IsLikelyGame_ErkenntSpiele(string shortcutName, string targetPath)
    {
        Assert.True(ShortcutImportScanner.IsLikelyGame(shortcutName, targetPath, ""));
    }

    [Theory]
    [InlineData("Battle.net", @"C:\Program Files\Battle.net\Battle.net.exe")]
    [InlineData("Epic Games Launcher", @"C:\Epic\EpicGamesLauncher.exe")]
    [InlineData("GOG Galaxy", @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe")]
    public void IsLikelyGame_StuftLauncherOhneStartparameterAlsUnwahrscheinlichEin(string shortcutName, string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsLikelyGame(shortcutName, targetPath, ""));
    }

    /// <summary>
    /// Entspricht der realen Verknüpfung eines GOG-Spiels: Ziel ist der Client,
    /// gestartet wird über die Parameter aber ein bestimmtes Spiel.
    /// </summary>
    [Fact]
    public void IsLikelyGame_ErkenntSpielStartUeberEinenStoreClientMitParametern()
    {
        bool isGame = ShortcutImportScanner.IsLikelyGame(
            "Frostpunk",
            @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe",
            @"/command=runGame /gameId=1648559910 /path=""D:\GOG\Frostpunk""");

        Assert.True(isGame);
    }

    [Theory]
    [InlineData("Spiel", @"C:\Games\Spiel\unins000.exe")]
    [InlineData("Spiel", @"C:\Games\Spiel\CrashReporter.exe")]
    [InlineData("Spiel", @"C:\Games\Spiel\SpielConfig.exe")]
    [InlineData("EA app-Updater", @"C:\Program Files\Electronic Arts\EA Desktop\EAUpdater.exe")]
    [InlineData("EA Error Reporter", @"C:\Program Files\Electronic Arts\EA Desktop\ErrorReporter.exe")]
    [InlineData("Game Launcher entfernen", @"C:\Programs\Game Launcher\unins000.exe")]
    public void IsLikelyGame_StuftHilfsprogrammeAlsUnwahrscheinlichEin(string shortcutName, string targetPath)
    {
        Assert.False(ShortcutImportScanner.IsLikelyGame(shortcutName, targetPath, ""));
    }

    [Fact]
    public void IsLikelyGame_StuftHilfsprogrammeAuchMitStartparameternAlsUnwahrscheinlichEin()
    {
        bool isGame = ShortcutImportScanner.IsLikelyGame(
            "Spiel deinstallieren",
            @"C:\Games\Spiel\setup.exe",
            "--uninstall");

        Assert.False(isGame);
    }

    [Fact]
    public void IsAlreadyKnown_ErkenntGleichenNamen()
    {
        var candidate = new ShortcutGameCandidate("Diablo IV", @"C:\Games\D4\game.exe", "", "", true);
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
            "",
            true);
        var existing = new[]
        {
            new Game { Id = "steam:620", Name = "Portal 2", InstallDirectory = @"C:\Games\Portal 2" }
        };

        Assert.True(ShortcutImportScanner.IsAlreadyKnown(candidate, existing));
    }

    [Fact]
    public void IsAlreadyKnown_MeldetUnbekannteSpieleNichtAlsVorhanden()
    {
        var candidate = new ShortcutGameCandidate("Neues Spiel", @"D:\Neu\neu.exe", "", "", true);
        var existing = new[]
        {
            new Game { Id = "steam:620", Name = "Portal 2", InstallDirectory = @"C:\Games\Portal 2" }
        };

        Assert.False(ShortcutImportScanner.IsAlreadyKnown(candidate, existing));
    }
}
