using System.Globalization;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public class SensorTemperatureReaderTests
{
    // Reale nvidia-smi-Ausgaben dieser Grafikkarte enthalten Dezimalpunkte.
    [Theory]
    [InlineData("de-DE", "43.52", 43.52f)]
    [InlineData("en-US", "43.52", 43.52f)]
    [InlineData("de-DE", "50.48", 50.48f)]
    [InlineData("en-US", "50.48", 50.48f)]
    [InlineData("de-DE", "43", 43f)]
    [InlineData("en-US", "43", 43f)]
    public void NvidiaTemperaturWirdUnabhängigVonDerSpracheGelesen(string culture, string value, float expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, SensorTemperatureReader.ParseNvidiaTemperature(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N/A")]
    [InlineData("43,52")]
    [InlineData("1,234")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("150")]
    [InlineData("-1")]
    public void UngültigeNvidiaTemperaturenBleibenOhneMesswert(string? value)
    {
        Assert.Null(SensorTemperatureReader.ParseNvidiaTemperature(value));
    }
}
