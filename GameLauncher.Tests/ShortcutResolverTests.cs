using System;
using System.IO;
using System.Runtime.InteropServices;
using GameLauncher.Services.Scanners;

namespace GameLauncher.Tests;

/// <summary>
/// Sichert die Zusage ab, dass eine aufgelöste Verknüpfung auf ein tatsächlich
/// vorhandenes Programm zeigt. IShellLink liefert den gespeicherten Pfad ohne
/// jede Prüfung zurück: eine Verknüpfung auf ein deinstalliertes Spiel sähe sonst
/// wie ein gültiges Ergebnis aus und landete als nicht startbarer Eintrag in der
/// Bibliothek.
/// </summary>
public sealed class ShortcutResolverTests : IDisposable
{
    private readonly string _workingDirectory;

    public ShortcutResolverTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "gl_lnk_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
    }

    [Fact]
    public void TryResolve_LiefertZielEinerGueltigenVerknuepfung()
    {
        string target = CreateProgram("Spiel.exe");
        string shortcut = CreateShortcut("Spiel.lnk", target, "-fullscreen");

        var resolved = ShortcutResolver.TryResolve(shortcut);

        Assert.NotNull(resolved);
        Assert.Equal(target, resolved!.TargetPath, ignoreCase: true);
        Assert.Equal("-fullscreen", resolved.Arguments);
    }

    [Fact]
    public void TryResolve_UeberspringtVerknuepfungAufDeinstalliertesProgramm()
    {
        string target = CreateProgram("Deinstalliert.exe");
        string shortcut = CreateShortcut("Altes Spiel.lnk", target, "");
        File.Delete(target);

        Assert.Null(ShortcutResolver.TryResolve(shortcut));
    }

    private string CreateProgram(string fileName)
    {
        string path = Path.Combine(_workingDirectory, fileName);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    /// <summary>
    /// Erzeugt eine Verknüpfung für den Test. Bewusst über den Windows Script Host:
    /// die getestete Klasse meidet ihn zwar zum Lesen, weil er in verwalteten
    /// Umgebungen abgeschaltet sein kann - zum Schreiben im Test ist er aber der
    /// Weg, der ohne eine zweite Kopie der COM-Deklarationen auskommt. Ist er
    /// abgeschaltet, schlägt der Test hier sichtbar fehl statt still zu bestehen.
    /// </summary>
    private string CreateShortcut(string fileName, string targetPath, string arguments)
    {
        string shortcutPath = Path.Combine(_workingDirectory, fileName);

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        Assert.NotNull(shellType);

        dynamic shell = Activator.CreateInstance(shellType!)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);

        return shortcutPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
