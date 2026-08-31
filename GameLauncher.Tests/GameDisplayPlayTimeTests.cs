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

    [Fact]
    public void DisplayPlayTime_MeldetKurzeMessungBeiErfasstenSpielen()
    {
        var game = new Game
        {
            Id = "steam:1",
            Name = "Spiel",
            IsManual = false,
            PlayTime = 0,
            LastPlayed = DateTime.Now
        };

        Assert.Equal("Gespielt (< 30 Sek.)", game.DisplayPlayTime);
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
    public void DisplayPlayTime_MeldetKurzeMessungBeiManuellenSpielenMitProgrammpfad()
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

        Assert.Equal("Gespielt (< 30 Sek.)", game.DisplayPlayTime);
    }

    [Fact]
    public void DisplayPlayTime_MeldetNieGespielteSpiele()
    {
        var game = new Game { Id = "manual_3", Name = "Spiel", PlayTime = 0, LastPlayed = null };

        Assert.Equal("Noch nie gespielt", game.DisplayPlayTime);
    }
}
