using System.Text.Json;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class UISettingsCloneTests
    {
        [Fact]
        public void Clone_UebernimmtAlleGespeichertenEinstellungen()
        {
            var settings = new UISettings
            {
                CardSize = CardSize.Large,
                ViewMode = ViewMode.List,
                LibrarySortMode = GameSortMode.PlayTime,
                LibraryFilter = "favorites",
                AnimationsEnabled = false,
                FontScale = 1.2,
                BackgroundImage = @"C:\Bilder\hintergrund.png",
                AutostartEnabled = true,
                AutoCheckUpdates = false,
                EncryptedSteamGridDbApiKey = "verschluesselt",
                MinimizeToTray = true,
                MinimizeOnGameStart = true,
                CloseOnGameStart = true,
                OverlayHotkeyCtrl = true,
                OverlayHotkeyAlt = false,
                OverlayHotkeyShift = true,
                OverlayHotkeyWin = true,
                OverlayHotkeyKey = "F9",
                FirstStart = false,
                LanguageCode = "de"
            };

            var clone = settings.Clone();

            // Vergleich über die JSON-Form, damit auch künftig ergänzte
            // Einstellungen ohne Anpassung des Tests abgedeckt sind.
            Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(clone));
        }

        [Fact]
        public void Clone_AenderungenAnDerKopieWirkenNichtAufsOriginal()
        {
            var settings = new UISettings { FontScale = 1.0, LanguageCode = "en" };

            var clone = settings.Clone();
            clone.FontScale = 1.4;
            clone.LanguageCode = "de";

            Assert.Equal(1.0, settings.FontScale);
            Assert.Equal("en", settings.LanguageCode);
        }
    }
}
