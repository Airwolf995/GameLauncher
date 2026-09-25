using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests;

/// <summary>
/// Wer in den Einstellungen einen Prozess ignoriert, erwartet, dass er ab dem
/// nächsten Durchlauf nicht mehr gezählt wird. Das Speichern der Einstellungen
/// löst kein Ereignis beim Spielzeitdienst aus.
///
/// Als laufendes Spiel dient der Testprozess selbst.
/// </summary>
public sealed class PlayTimeServiceIgnoredProcessesTests : IDisposable
{
    private readonly string _configPath;

    public PlayTimeServiceIgnoredProcessesTests()
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherIgnoredProcesses_").FullName;
        _configPath = Path.Combine(root, "game_launcher_config.json");
    }

    public void Dispose()
    {
        var directory = Directory.GetParent(_configPath);
        if (directory != null && directory.Exists)
        {
            try { directory.Delete(recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void NeuIgnorierterProzessWirdAbDemNaechstenDurchlaufNichtMehrErfasst()
    {
        string processName = Process.GetCurrentProcess().ProcessName;
        var games = new ObservableCollection<Game>
        {
            new()
            {
                Id = "manual_testprozess",
                Name = "Testprozess",
                IsManual = true,
                LaunchType = "exe",
                ExecutableName = processName
            }
        };
        using var manager = new GameManager(_configPath);
        manager.UpdateConfig(config => config.IgnoredProcesses = []);
        using var service = new PlayTimeService(manager, games);

        service.Start();
        service.RunTick();
        Assert.NotNull(service.ActiveGame);

        // So übernimmt das Einstellungsfenster die Liste.
        manager.UpdateConfig(config => config.IgnoredProcesses = [processName]);
        service.RunTick();

        Assert.Null(service.ActiveGame);
    }
}
