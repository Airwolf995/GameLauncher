using System;
using GameLauncher.Models;

namespace GameLauncher.Tests;

/// <summary>
/// Ein Fehlereintrag muss zur Ursache führen: Bei einem Absturz ist die
/// Stelle im Code und die auslösende innere Ausnahme wichtiger als die
/// Meldung allein.
/// </summary>
public sealed class LoggerErrorFormatTests
{
    [Fact]
    public void FehlereintragEnthaeltStacktraceUndInnereAusnahme()
    {
        Exception exception = CaptureThrown();

        string entry = Logger.FormatError("Unhandled Application Crash", exception);

        Assert.Contains("Unhandled Application Crash", entry);
        Assert.Contains("Äußere Meldung", entry);
        Assert.Contains("Innere Ursache", entry);
        Assert.Contains(nameof(ThrowWrapped), entry);
    }

    [Fact]
    public void FehlereintragOhneAusnahmeBestehtNurAusDerMeldung()
    {
        Assert.Equal("ERROR: Nur Meldung", Logger.FormatError("Nur Meldung", null));
    }

    private static Exception CaptureThrown()
    {
        try
        {
            ThrowWrapped();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Es wurde keine Ausnahme ausgelöst.");
    }

    private static void ThrowWrapped()
    {
        try
        {
            throw new ArgumentException("Innere Ursache");
        }
        catch (ArgumentException inner)
        {
            throw new InvalidOperationException("Äußere Meldung", inner);
        }
    }
}
