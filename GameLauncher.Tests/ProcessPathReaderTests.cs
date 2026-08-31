using System.Diagnostics;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public sealed class ProcessPathReaderTests
{
    [Fact]
    public void TryGetExecutablePath_LiefertDenPfadDesEigenenProzesses()
    {
        using var current = Process.GetCurrentProcess();

        string? path = ProcessPathReader.TryGetExecutablePath(current.Id);

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"Der gemeldete Pfad muss existieren: {path}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryGetExecutablePath_LiefertOhneGueltigeKennungKeinenPfad(int processId)
    {
        Assert.Null(ProcessPathReader.TryGetExecutablePath(processId));
    }

    [Fact]
    public void TryGetExecutablePath_LiefertFuerBeendeteProzesseKeinenPfad()
    {
        // Eine Kennung, die mit hoher Wahrscheinlichkeit zu keinem Prozess gehört.
        Assert.Null(ProcessPathReader.TryGetExecutablePath(int.MaxValue - 1));
    }

    [Fact]
    public void TryGetExecutablePath_ErmitteltMehrPfadeAlsMainModule()
    {
        int viaReader = 0;
        int viaMainModule = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrEmpty(ProcessPathReader.TryGetExecutablePath(process.Id)))
                {
                    viaReader++;
                }

                try
                {
                    if (!string.IsNullOrEmpty(process.MainModule?.FileName))
                    {
                        viaMainModule++;
                    }
                }
                catch
                {
                    // Zugriff verweigert: genau der Fall, den die Abfrage vermeidet.
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        Assert.True(
            viaReader >= viaMainModule,
            $"Die Abfrage darf nicht weniger Pfade liefern als MainModule: {viaReader} statt {viaMainModule}.");
    }
}
