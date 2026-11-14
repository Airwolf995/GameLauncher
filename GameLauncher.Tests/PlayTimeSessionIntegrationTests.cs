using System.Diagnostics;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests;

public sealed class PlayTimeSessionIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("GameLauncherSessions_").FullName;

    [Fact]
    public async Task GemesseneSekundenErreichenGesamtzeitTagesverlaufUndAbschluss()
    {
        using var process = Process.GetCurrentProcess();
        var game = new Game
        {
            Id = "test", Name = "Testspiel", IsManual = true,
            LaunchType = "exe", ExecutableName = process.ProcessName
        };
        var time = new ManualPlayTimeProvider();
        using var manager = new GameManager(Path.Combine(_root, "config.json"));
        manager.UpdateConfig(config => config.IgnoredProcesses = []);
        using var service = new PlayTimeService(manager, [game], null, time);
        service.Start();
        service.RunTick();
        Assert.Same(game, service.ActiveGame);
        Assert.NotNull(service.SessionStartTime);
        Assert.Equal(0, game.PlayTime);

        time.Advance(TimeSpan.FromMilliseconds(3750));
        service.RunTick();
        Assert.Equal(3, game.PlayTime);
        time.Advance(TimeSpan.FromMilliseconds(2250));
        await service.StopAsync();

        Assert.Equal(6, game.PlayTime);
        Assert.Equal(6, manager.Config.PlayTime[game.Id].Seconds);
        Assert.Equal(6, manager.GetDailyPlayTime(game.Id, time.GetLocalNow().DateTime).Sum());
        Assert.Null(service.ActiveGame);
        Assert.Null(service.SessionStartTime);
    }

    [Fact]
    public async Task StartauftragBeschleunigtDieErkennungOhneUnbestätigteSpielzeitZuBuchen()
    {
        var game = new Game
        {
            Id = "test", Name = "Nicht gestartetes Spiel", IsManual = true,
            LaunchType = "exe", ExecutableName = "GameLauncher_Test_NichtVorhanden"
        };
        var time = new ManualPlayTimeProvider();
        using var manager = new GameManager(Path.Combine(_root, "config.json"));
        using var service = new PlayTimeService(manager, [game], null, time);
        service.Start();
        service.RunTick();
        Assert.Equal(30_000, service.CurrentTickIntervalMs);

        service.NotifyGameLaunched(game);
        Assert.Equal(2_000, service.CurrentTickIntervalMs);
        time.Advance(TimeSpan.FromSeconds(10));
        service.RunTick();
        Assert.Null(service.ActiveGame);
        Assert.Equal(0, game.PlayTime);
        Assert.Equal(2_000, service.CurrentTickIntervalMs);

        time.Advance(TimeSpan.FromMinutes(2));
        service.RunTick();
        Assert.Equal(30_000, service.CurrentTickIntervalMs);
        Assert.Equal(0, game.PlayTime);
        Assert.Empty(manager.Config.PlayTime);
        await service.StopAsync();
    }

    [Fact]
    public async Task VerzögerterNachfolgerWirdImStartfensterErkanntOhneDieProzesslückeZuZählen()
    {
        string processDirectory = Path.Combine(_root, "Prozesse");
        Directory.CreateDirectory(processDirectory);
        string bootstrapPath = Path.Combine(processDirectory, "TestStartprogramm.exe");
        string gamePath = Path.Combine(processDirectory, "TestSpiel.exe");
        string commandProcessor = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        File.Copy(commandProcessor, bootstrapPath);
        File.Copy(commandProcessor, gamePath);

        using var bootstrap = StartWaitingProcess(bootstrapPath);
        Process? successor = null;
        try
        {
            var game = new Game
            {
                Id = "test", Name = "Spiel mit Startprogramm", IsManual = true,
                LaunchType = "exe", Path = bootstrapPath, InstallDirectory = processDirectory
            };
            var time = new ManualPlayTimeProvider();
            using var manager = new GameManager(Path.Combine(_root, "config.json"));
            manager.UpdateConfig(config => config.IgnoredProcesses = []);
            using var service = new PlayTimeService(manager, [game], null, time);
            service.Start();
            service.NotifyGameLaunched(game);
            service.RunTick();
            Assert.Same(game, service.ActiveGame);

            time.Advance(TimeSpan.FromSeconds(2));
            service.RunTick();
            bootstrap.Kill(entireProcessTree: true);
            Assert.True(bootstrap.WaitForExit(5000));
            time.Advance(TimeSpan.FromSeconds(2));
            service.RunTick();
            Assert.Null(service.ActiveGame);
            Assert.Equal(2_000, service.CurrentTickIntervalMs);
            int beforeGap = game.PlayTime;

            // Mehrere schnelle Durchläufe ohne Spielprozess dürfen weder in
            // den Leerlauf wechseln noch die Wartezeit als Spielzeit verbuchen.
            time.Advance(TimeSpan.FromSeconds(6));
            service.RunTick();
            Assert.Equal(beforeGap, game.PlayTime);
            Assert.Equal(2_000, service.CurrentTickIntervalMs);

            successor = StartWaitingProcess(gamePath);
            time.Advance(TimeSpan.FromSeconds(2));
            service.RunTick();
            Assert.False(successor.HasExited);
            Assert.Same(game, service.ActiveGame);
            Assert.Equal(beforeGap, game.PlayTime);

            time.Advance(TimeSpan.FromSeconds(3));
            service.RunTick();
            Assert.Equal(beforeGap + 3, game.PlayTime);
            Assert.Equal(game.PlayTime, manager.Config.PlayTime[game.Id].Seconds);
            Assert.Equal(game.PlayTime, manager.GetDailyPlayTime(game.Id, time.GetLocalNow().DateTime).Sum());

            successor.Kill(entireProcessTree: true);
            Assert.True(successor.WaitForExit(5000));
            time.Advance(TimeSpan.FromSeconds(2));
            service.RunTick();
            int afterExit = game.PlayTime;
            // Das ursprüngliche Startfenster endet nach insgesamt 120 Sekunden,
            // auch wenn zwischendurch beide Prozesse erkannt wurden.
            time.Advance(TimeSpan.FromSeconds(103));
            service.RunTick();
            Assert.Null(service.ActiveGame);
            Assert.Equal(30_000, service.CurrentTickIntervalMs);
            Assert.Equal(afterExit, game.PlayTime);
            await service.StopAsync();
        }
        finally
        {
            if (!bootstrap.HasExited) { bootstrap.Kill(entireProcessTree: true); bootstrap.WaitForExit(5000); }
            if (successor != null)
            {
                if (!successor.HasExited) { successor.Kill(entireProcessTree: true); successor.WaitForExit(5000); }
                successor.Dispose();
            }
        }
    }

    private static Process StartWaitingProcess(string executablePath) => Process.Start(new ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = "/d /q /k",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    })!;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
