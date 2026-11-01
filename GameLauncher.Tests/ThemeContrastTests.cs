using System.Windows.Media;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public sealed class ThemeContrastTests
{
    public static IEnumerable<object[]> Themes => Constants.UI.ThemeColors
        .Select(theme => new object[] { theme.Key, theme.Value });

    [Theory]
    [MemberData(nameof(Themes))]
    public void AkzentschriftBewahrtDasBestehendeThemeDesign(string theme, string colorCode)
    {
        var accent = (Color)ColorConverter.ConvertFromString(colorCode);
        Color expected = theme == "White" ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;

        Assert.Equal(expected, UISettingsService.GetReadableForeground(accent));
    }
}
