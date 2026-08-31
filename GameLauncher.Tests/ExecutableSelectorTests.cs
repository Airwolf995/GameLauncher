using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

public sealed class ExecutableSelectorTests : IDisposable
{
    private readonly string _gameDirectory;

    public ExecutableSelectorTests()
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherExeTests_").FullName;
        _gameDirectory = Directory.CreateDirectory(Path.Combine(root, "Assassins Creed Valhalla")).FullName;
    }

    public void Dispose()
    {
        var root = Directory.GetParent(_gameDirectory);
        if (root != null && root.Exists)
        {
            root.Delete(recursive: true);
        }
    }

    private void CreateExecutable(string fileName, int sizeInBytes)
    {
        File.WriteAllBytes(Path.Combine(_gameDirectory, fileName), new byte[sizeInBytes]);
    }

    [Fact]
    public void FindPrimaryExecutable_BevorzugtDenZumOrdnerPassendenNamen()
    {
        CreateExecutable("ACValhalla.exe", 100);
        CreateExecutable("AnticheatService.exe", 5000);

        string result = ExecutableSelector.FindPrimaryExecutable(_gameDirectory);

        Assert.Equal("ACValhalla.exe", Path.GetFileName(result));
    }

    [Fact]
    public void FindPrimaryExecutable_NimmtOhneNamenstrefferDieGroessteDatei()
    {
        CreateExecutable("AAAHelper.exe", 100);
        CreateExecutable("Spielprogramm.exe", 9000);

        string result = ExecutableSelector.FindPrimaryExecutable(_gameDirectory);

        Assert.Equal("Spielprogramm.exe", Path.GetFileName(result));
    }

    [Fact]
    public void FindPrimaryExecutable_BeachtetAusgeschlosseneNamensbestandteile()
    {
        CreateExecutable("unins000.exe", 9000);
        CreateExecutable("Spiel.exe", 100);

        string result = ExecutableSelector.FindPrimaryExecutable(_gameDirectory, "unins");

        Assert.Equal("Spiel.exe", Path.GetFileName(result));
    }

    [Fact]
    public void FindPrimaryExecutable_LiefertLeerenPfadOhnePassendeDatei()
    {
        CreateExecutable("unins000.exe", 100);

        string result = ExecutableSelector.FindPrimaryExecutable(_gameDirectory, "unins");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FindPrimaryExecutable_LiefertLeerenPfadFuerFehlendesVerzeichnis()
    {
        string result = ExecutableSelector.FindPrimaryExecutable(
            Path.Combine(_gameDirectory, "NichtVorhanden"));

        Assert.Equal(string.Empty, result);
    }
}
