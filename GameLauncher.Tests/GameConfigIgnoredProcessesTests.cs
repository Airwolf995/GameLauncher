using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Tests;

public sealed class GameConfigIgnoredProcessesTests
{
    /// <summary>
    /// Eine geänderte Standardliste darf bestehende Konfigurationen nicht
    /// verändern: Die gespeicherte Liste gilt unverändert, auch mit Einträgen,
    /// die im Standard nicht mehr stehen.
    /// </summary>
    [Fact]
    public void GespeicherteListeErsetztDenStandardVollstaendig()
    {
        const string json = """{ "ignored_processes": ["Steam.exe", "UbisoftConnect.exe", "wallpaper64.exe"] }""";

        var config = JsonSerializer.Deserialize<GameConfig>(json)!;

        Assert.Equal(["Steam.exe", "UbisoftConnect.exe", "wallpaper64.exe"], config.IgnoredProcesses);
    }

    [Fact]
    public void NeueKonfigurationIgnoriertNurDenBsgLauncher()
    {
        Assert.Equal(["BsgLauncher.exe"], new GameConfig().IgnoredProcesses);
    }
}
