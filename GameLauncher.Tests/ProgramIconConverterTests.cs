using System.Globalization;
using System.Windows.Media;
using GameLauncher.Converters;

namespace GameLauncher.Tests;

public sealed class ProgramIconConverterTests
{
    private static object? Convert(string? path) =>
        new ProgramIconConverter().Convert(path!, typeof(ImageSource), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_LiefertDasSymbolEinerVorhandenenProgrammdatei()
    {
        string exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        object? icon = Convert(exePath);

        Assert.NotNull(icon);
        Assert.IsAssignableFrom<ImageSource>(icon);
        Assert.True(((ImageSource)icon).IsFrozen, "Das Symbol muss eingefroren sein, damit es threadübergreifend nutzbar bleibt.");
    }

    [Fact]
    public void Convert_LiefertDasselbeSymbolAusDemZwischenspeicher()
    {
        string exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        object? first = Convert(exePath);
        object? second = Convert(exePath);

        Assert.Same(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Nicht\Vorhanden\programm.exe")]
    public void Convert_LiefertOhneVerwertbarenPfadKeinSymbol(string? path)
    {
        Assert.Null(Convert(path));
    }

    [Fact]
    public void ConvertBack_WirdNichtUnterstuetzt()
    {
        var converter = new ProgramIconConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
