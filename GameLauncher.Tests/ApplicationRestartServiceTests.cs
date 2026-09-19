using GameLauncher.Services.Settings;

namespace GameLauncher.Tests;

/// <summary>
/// Der Schalter, mit dem die neue Sitzung auf das Ende der alten wartet. Ohne
/// dieses Warten liefe sie in die Einzelinstanz-Sperre und beendete sich
/// sofort wieder mit dem Hinweis, die Anwendung laufe bereits.
/// </summary>
public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void BuildWaitArguments_WirdVonDerGegenseiteWiederGelesen()
    {
        string arguments = ApplicationRestartService.BuildWaitArguments(4711);

        Assert.Equal(4711, ApplicationRestartService.TryReadProcessIdToWaitFor(arguments.Split(' ')));
    }

    [Fact]
    public void TryReadProcessIdToWaitFor_MeldetOhneSchalterNichts()
    {
        Assert.Null(ApplicationRestartService.TryReadProcessIdToWaitFor([]));
        Assert.Null(ApplicationRestartService.TryReadProcessIdToWaitFor(["--anderes", "123"]));
    }

    /// <summary>
    /// Fehlt die Kennung oder ist sie unbrauchbar, wird nicht gewartet. Ein
    /// Warten auf nichts wuerde den Start nur um das Zeitlimit verzoegern.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("keinezahl")]
    [InlineData("0")]
    [InlineData("-5")]
    public void TryReadProcessIdToWaitFor_MeldetUnbrauchbareKennungNicht(string value)
    {
        string[] arguments = value.Length == 0
            ? ["--warte-auf-prozess"]
            : ["--warte-auf-prozess", value];

        Assert.Null(ApplicationRestartService.TryReadProcessIdToWaitFor(arguments));
    }

    [Fact]
    public void TryReadProcessIdToWaitFor_FindetDenSchalterAuchHinterAnderenArgumenten()
    {
        Assert.Equal(
            99,
            ApplicationRestartService.TryReadProcessIdToWaitFor(["--irgendwas", "--warte-auf-prozess", "99"]));
    }
}
