using System.Diagnostics;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public class PlayTimeProcessMonitorTests
{
    [Fact]
    public void LaufenderProzessWirdOhneErneuteProzesssucheÜberwacht()
    {
        int searches = 0;
        using var current = Process.GetCurrentProcess();
        using var monitor = new PlayTimeProcessMonitor(() =>
        {
            searches++;
            return [Process.GetProcessById(current.Id)];
        });
        var index = CreateIndex(current.ProcessName);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("spiel", monitor.Poll(index, ignored, true, DateTime.Now).Keys);
        Assert.Contains("spiel", monitor.Poll(index, ignored, false, DateTime.Now).Keys);
        Assert.Equal(1, searches);

        ignored.Add(current.ProcessName + ".exe");
        Assert.Empty(monitor.Poll(index, ignored, false, DateTime.Now));
    }

    [Fact]
    public void ProzesswechselWirdDirektErkanntUndProzessendeBeendetDieErkennung()
    {
        using var first = StartWaitingProcess();
        using var second = StartWaitingProcess();
        int searches = 0;
        Process? available = first;
        using var monitor = new PlayTimeProcessMonitor(() =>
        {
            searches++;
            return available == null ? [] : [Process.GetProcessById(available.Id)];
        });
        var index = CreateIndex(first.ProcessName);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Assert.Contains("spiel", monitor.Poll(index, ignored, true, DateTime.Now).Keys);
            first.Kill(entireProcessTree: true);
            Assert.True(first.WaitForExit(5000));
            available = second;
            Assert.Contains("spiel", monitor.Poll(index, ignored, false, DateTime.Now).Keys);
            Assert.Equal(2, searches);

            second.Kill(entireProcessTree: true);
            Assert.True(second.WaitForExit(5000));
            available = null;
            Assert.Empty(monitor.Poll(index, ignored, false, DateTime.Now));
        }
        finally
        {
            if (!first.HasExited) { first.Kill(entireProcessTree: true); first.WaitForExit(5000); }
            if (!second.HasExited) { second.Kill(entireProcessTree: true); second.WaitForExit(5000); }
        }
    }

    private static Process StartWaitingProcess() => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"",
        UseShellExecute = false,
        CreateNoWindow = true
    })!;

    private static PlayTimeMatchIndex CreateIndex(string name)
    {
        var index = new PlayTimeMatchIndex();
        index.Rebuild([new Game
        {
            Id = "spiel", Name = "Testspiel", IsManual = true,
            LaunchType = "exe", ExecutableName = name
        }]);
        return index;
    }
}
