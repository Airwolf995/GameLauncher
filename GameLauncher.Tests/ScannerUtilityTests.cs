using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class ScannerUtilityTests : IDisposable
{
    private readonly string _temporaryDirectory = Directory.CreateTempSubdirectory("GameLauncherScannerTests_").FullName;

    [Fact]
    public void NormalizeDistinct_NormalisiertUndEntferntDoppeltePfade()
    {
        string pathWithSeparator = _temporaryDirectory + Path.DirectorySeparatorChar;

        List<string> result = ScannerPathUtility.NormalizeDistinct(
            [_temporaryDirectory, pathWithSeparator, $"  {_temporaryDirectory}  "]);

        Assert.Single(result);
        Assert.Equal(
            Path.GetFullPath(_temporaryDirectory).TrimEnd(Path.DirectorySeparatorChar),
            result[0],
            ignoreCase: true);
    }

    [Fact]
    public void Normalize_BehältDenTrennerEinerLaufwerkswurzel()
    {
        string rootPath = Path.GetPathRoot(_temporaryDirectory)!;

        string result = ScannerPathUtility.Normalize(rootPath);

        Assert.Equal(rootPath, result, ignoreCase: true);
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void AddExistingDirectory_IgnoriertFehlendeUndDoppeltePfade()
    {
        var paths = new List<string>();

        ScannerPathUtility.AddExistingDirectory(paths, _temporaryDirectory);
        ScannerPathUtility.AddExistingDirectory(paths, _temporaryDirectory + Path.DirectorySeparatorChar);
        ScannerPathUtility.AddExistingDirectory(paths, Path.Combine(_temporaryDirectory, "fehlt"));

        Assert.Single(paths);
    }

    [Fact]
    public void FindPrimaryExecutable_WaehltDeterministischPlausibleDatei()
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, "z_game.exe"), string.Empty);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "a_crash.exe"), string.Empty);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "b_game.exe"), string.Empty);

        string result = ExecutableSelector.FindPrimaryExecutable(_temporaryDirectory, "crash");

        Assert.Equal("b_game.exe", Path.GetFileName(result));
    }

    [Fact]
    public void GetLibraryDirectories_FasstSpielordnerNachBibliothekZusammen()
    {
        string libraryDirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "EA Games")).FullName;
        string firstGame = Directory.CreateDirectory(Path.Combine(libraryDirectory, "Spiel A")).FullName;
        string secondGame = Directory.CreateDirectory(Path.Combine(libraryDirectory, "Spiel B")).FullName;

        List<string> result = ScannerPathUtility.GetLibraryDirectories([firstGame, secondGame]);

        string detectedLibrary = Assert.Single(result);
        Assert.Equal(libraryDirectory, detectedLibrary, ignoreCase: true);
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
