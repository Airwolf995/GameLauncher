using GameLauncher.Models;
using GameLauncher.Services.MainWindow;

namespace GameLauncher.Tests
{
    public class UiSettingsSnapshotTests
    {
        private static UiSettingsSnapshot Snapshot(System.Action<UISettings>? change = null)
        {
            var settings = new UISettings();
            change?.Invoke(settings);
            return UiSettingsSnapshot.From(settings);
        }

        [Fact]
        public void FirstApply_AppliesEveryPart()
        {
            var current = Snapshot();

            Assert.True(current.ViewDiffersFrom(null));
            Assert.True(current.AnimationsDifferFrom(null));
            Assert.True(current.FontScaleDiffersFrom(null));
            Assert.True(current.BackgroundImageDiffersFrom(null));
        }

        [Fact]
        public void UnchangedSettings_AreEqualAndApplyNothing()
        {
            var previous = Snapshot();
            var current = Snapshot();

            Assert.Equal(previous, current);
            Assert.False(current.ViewDiffersFrom(previous));
            Assert.False(current.AnimationsDifferFrom(previous));
            Assert.False(current.FontScaleDiffersFrom(previous));
            Assert.False(current.BackgroundImageDiffersFrom(previous));
        }

        /// <summary>
        /// Der Zweck des Vergleichs: Ein neues Hintergrundbild darf den
        /// Ansichtsmodus nicht neu setzen, sonst würden die Karten neu animiert.
        /// </summary>
        [Fact]
        public void ChangedBackgroundImage_LeavesViewAlone()
        {
            var previous = Snapshot();
            var current = Snapshot(s => s.BackgroundImage = @"C:\Bilder\wald.jpg");

            Assert.True(current.BackgroundImageDiffersFrom(previous));
            Assert.False(current.ViewDiffersFrom(previous));
            Assert.False(current.AnimationsDifferFrom(previous));
            Assert.False(current.FontScaleDiffersFrom(previous));
        }

        [Theory]
        [InlineData(ViewMode.List, CardSize.Medium)]
        [InlineData(ViewMode.Cards, CardSize.Large)]
        public void ChangedViewModeOrCardSize_ChangesView(ViewMode viewMode, CardSize cardSize)
        {
            var previous = Snapshot(s =>
            {
                s.ViewMode = ViewMode.Cards;
                s.CardSize = CardSize.Medium;
            });
            var current = Snapshot(s =>
            {
                s.ViewMode = viewMode;
                s.CardSize = cardSize;
            });

            Assert.True(current.ViewDiffersFrom(previous));
        }

        [Fact]
        public void FontScale_IgnoresRoundingNoise()
        {
            var previous = Snapshot(s => s.FontScale = 1.1);
            var noise = Snapshot(s => s.FontScale = 1.1 + 1e-9);
            var changed = Snapshot(s => s.FontScale = 1.2);

            Assert.False(noise.FontScaleDiffersFrom(previous));
            Assert.True(changed.FontScaleDiffersFrom(previous));
        }

        /// <summary>
        /// Das Tastenkürzel gehört zum gemerkten Stand, ändert aber keinen
        /// Darstellungsteil.
        /// </summary>
        [Fact]
        public void ChangedHotkey_MakesSnapshotDifferWithoutTouchingDisplay()
        {
            var previous = Snapshot();
            var current = Snapshot(s => s.OverlayHotkeyKey = "O");

            Assert.NotEqual(previous, current);
            Assert.False(current.ViewDiffersFrom(previous));
            Assert.False(current.AnimationsDifferFrom(previous));
            Assert.False(current.FontScaleDiffersFrom(previous));
            Assert.False(current.BackgroundImageDiffersFrom(previous));
        }

        [Fact]
        public void MissingTexts_CountAsEmpty()
        {
            var withNulls = Snapshot(s =>
            {
                s.BackgroundImage = null!;
                s.OverlayHotkeyKey = null!;
            });
            var withEmpty = Snapshot(s =>
            {
                s.BackgroundImage = "";
                s.OverlayHotkeyKey = "";
            });

            Assert.Equal(withEmpty, withNulls);
        }
    }
}
