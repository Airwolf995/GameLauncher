using System;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    /// <summary>
    /// Zuletzt im Hauptfenster angewendeter Stand der Oberflächeneinstellungen.
    /// Die Vorschau im Einstellungsfenster wendet bei jeder Änderung alles erneut
    /// an; der Vergleich sorgt dafür, dass nur die tatsächlich geänderten Teile
    /// neu gesetzt werden. So löst etwa ein anderes Hintergrundbild keinen
    /// Ansichtswechsel mit neuer Kartenanimation aus.
    /// </summary>
    internal sealed record UiSettingsSnapshot(
        CardSize CardSize,
        ViewMode ViewMode,
        bool AnimationsEnabled,
        double FontScale,
        string BackgroundImage,
        bool OverlayHotkeyCtrl,
        bool OverlayHotkeyAlt,
        bool OverlayHotkeyShift,
        bool OverlayHotkeyWin,
        string OverlayHotkeyKey)
    {
        private const double FontScaleTolerance = 0.0001;

        public static UiSettingsSnapshot From(UISettings settings) =>
            new(
                settings.CardSize,
                settings.ViewMode,
                settings.AnimationsEnabled,
                settings.FontScale,
                settings.BackgroundImage ?? "",
                settings.OverlayHotkeyCtrl,
                settings.OverlayHotkeyAlt,
                settings.OverlayHotkeyShift,
                settings.OverlayHotkeyWin,
                settings.OverlayHotkeyKey ?? "");

        public bool ViewDiffersFrom(UiSettingsSnapshot? previous) =>
            previous == null ||
            previous.ViewMode != ViewMode ||
            previous.CardSize != CardSize;

        public bool AnimationsDifferFrom(UiSettingsSnapshot? previous) =>
            previous == null || previous.AnimationsEnabled != AnimationsEnabled;

        public bool FontScaleDiffersFrom(UiSettingsSnapshot? previous) =>
            previous == null || Math.Abs(previous.FontScale - FontScale) > FontScaleTolerance;

        public bool BackgroundImageDiffersFrom(UiSettingsSnapshot? previous) =>
            previous == null || !string.Equals(previous.BackgroundImage, BackgroundImage, StringComparison.Ordinal);
    }
}
