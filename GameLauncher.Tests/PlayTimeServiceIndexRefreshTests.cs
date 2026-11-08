using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections;
using System.Diagnostics;
using System.IO;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests;

/// <summary>
/// Der Suchindex wird aus der Spielesammlung der Oberfläche aufgebaut. Hinzufügen,
/// Importieren und Löschen manueller Spiele lassen das globale Aktualisierungsereignis
/// bewusst aus, damit die Bibliothek nicht komplett neu lädt. Der Index muss die
/// Änderung trotzdem mitbekommen - sonst sammelt ein gelöschtes Spiel weiter Spielzeit
/// und legt die gerade entfernten Einträge in der Konfiguration neu an.
///
/// Als laufendes Spiel dient der Testprozess selbst; damit prüft der Test die
/// Zuordnung tatsächlich und nicht nur einen internen Zustand.
/// </summary>
public sealed class PlayTimeServiceIndexRefreshTests : IDisposable
{
    private readonly string _configPath;

    public PlayTimeServiceIndexRefreshTests()
    {
        string root = Directory.CreateTempSubdirectory("GameLauncherIndexRefresh_").FullName;
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
    public void EntferntesSpielWirdNichtWeiterErfasst()
    {
        var games = new ObservableCollection<Game> { CreateGameForCurrentProcess() };
        using var manager = new GameManager(_configPath);
        using var service = new PlayTimeService(manager, games);

        service.Start();
        service.RunTick();
        Assert.NotNull(service.ActiveGame);

        games.RemoveAt(0);
        service.RunTick();

        Assert.Null(service.ActiveGame);
    }

    [Fact]
    public void HinzugefuegtesSpielWirdOhneGlobalesEreignisErfasst()
    {
        var games = new ObservableCollection<Game>();
        using var manager = new GameManager(_configPath);
        using var service = new PlayTimeService(manager, games);

        service.Start();
        service.RunTick();
        Assert.Null(service.ActiveGame);

        games.Add(CreateGameForCurrentProcess());
        service.RunTick();

        Assert.NotNull(service.ActiveGame);
    }

    private static Game CreateGameForCurrentProcess() => new()
    {
        Id = "manual_testprozess",
        Name = "Testprozess",
        IsManual = true,
        LaunchType = "exe",
        ExecutableName = Process.GetCurrentProcess().ProcessName
    };

    [Fact]
    public void ÄnderungWährendDesIndexaufbausBleibtFürDenNächstenTickErhalten()
    {
        var games = new ChangingGames(CreateGameForCurrentProcess());
        using var manager = new GameManager(_configPath);
        using var service = new PlayTimeService(manager, games);
        service.Start();
        service.RunTick();
        Assert.NotNull(service.ActiveGame);

        // Der Snapshot enthält noch das Spiel, unmittelbar danach kommt die
        // Änderungsmeldung. Der nächste Tick muss erneut einen Snapshot lesen.
        games.RemoveAfterNextSnapshot();
        service.RunTick();
        service.RunTick();

        Assert.Null(service.ActiveGame);
    }

    private sealed class ChangingGames(Game game) : IEnumerable<Game>, INotifyCollectionChanged
    {
        private readonly List<Game> _games = [game];
        private bool _removeAfterSnapshot;
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void RemoveAfterNextSnapshot()
        {
            _removeAfterSnapshot = true;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public IEnumerator<Game> GetEnumerator()
        {
            foreach (var item in _games.ToArray()) yield return item;
            if (_removeAfterSnapshot)
            {
                _removeAfterSnapshot = false;
                _games.Clear();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
