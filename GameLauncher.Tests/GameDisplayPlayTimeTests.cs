using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Tests;

public sealed class GameDisplayPlayTimeTests
{
    public GameDisplayPlayTimeTests()
    {
        // Die Anzeige enthält sprachabhängige Texte, daher wird die Sprache
        // festgelegt. Kein anderer Test prüft lokalisierte Texte, sodass die
        // Umschaltung des gemeinsamen Dienstes hier unkritisch ist.
        LocalizationService.Instance.ApplyLanguageCode("de");
    }

    [Fact]
    public void DisplayPlayTime_NennntGemesseneZeit()
    {
        var game = new Game { Id = "steam:1", Name = "Spiel", PlayTime = 3720, LastPlayed = DateTime.Now };

        Assert.Equal("1 Std. 2 Min.", game.DisplayPlayTime);
    }

    [Theory]
    [InlineData(1050, "17 Min. 30 Sek.")]
    [InlineData(45, "45 Sek.")]
    public void FormatDuration_NenntKurzeDauernMitSekunden(int seconds, string expected)
    {
        Assert.Equal(expected, Game.FormatDuration(seconds));
    }

    [Fact]
    public void DisplayPlayTime_NenntOhneErfassteSpielzeitNurDenStart()
    {
        var game = new Game
        {
            Id = "steam:1",
            Name = "Spiel",
            IsManual = false,
            PlayTime = 0,
            LastPlayed = DateTime.Now
        };

        Assert.Equal("Gestartet (keine Spielzeit erfasst)", game.DisplayPlayTime);
    }

    /// <summary>
    /// Ohne Zeiterfassung darf die Anzeige keine kurze Spielzeit behaupten.
    /// </summary>
    [Fact]
    public void DisplayPlayTime_BehauptetOhneZeitmessungKeineSpielzeit()
    {
        var game = new Game
        {
            Id = "manual_1",
            Name = "Eintrag",
            IsManual = true,
            LaunchType = "uri",
            PlayTime = 0,
            LastPlayed = DateTime.Now
        };

        Assert.Equal("Gestartet (keine Zeitmessung)", game.DisplayPlayTime);
    }

    [Fact]
    public void DisplayPlayTime_NenntOhneErfassteSpielzeitNurDenStartBeiManuellenSpielenMitProgrammpfad()
    {
        var game = new Game
        {
            Id = "manual_2",
            Name = "Spiel",
            IsManual = true,
            LaunchType = "exe",
            PlayTime = 0,
            LastPlayed = DateTime.Now
        };

        Assert.Equal("Gestartet (keine Spielzeit erfasst)", game.DisplayPlayTime);
    }

    [Fact]
    public void DisplayPlayTime_MeldetNieGespielteSpiele()
    {
        var game = new Game { Id = "manual_3", Name = "Spiel", PlayTime = 0, LastPlayed = null };

        Assert.Equal("Noch nie gespielt", game.DisplayPlayTime);
    }
}
