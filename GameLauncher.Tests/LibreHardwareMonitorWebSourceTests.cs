using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class LibreHardwareMonitorWebSourceTests
    {
        /// <summary>
        /// Die Anwendung liefert ihre Werte als Text mit Einheit und in der
        /// Zahlenschreibweise ihrer Oberflächensprache.
        /// </summary>
        [Theory]
        [InlineData("59,0 °C", 59.0f)]
        [InlineData("59.0 °C", 59.0f)]
        [InlineData("26,0 %", 26.0f)]
        [InlineData("16376,0 MB", 16376.0f)]
        [InlineData("1,5 V", 1.5f)]
        [InlineData("-5,0 °C", -5.0f)]
        [InlineData("0,0 %", 0f)]
        public void TryParseValue_ReadsNumberBeforeUnit(string rawValue, float expected)
        {
            Assert.True(LibreHardwareMonitorWebSource.TryParseValue(rawValue, out float value));
            Assert.Equal(expected, value, precision: 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("n/a")]
        [InlineData("°C")]
        public void TryParseValue_RejectsValuesWithoutNumber(string? rawValue)
        {
            Assert.False(LibreHardwareMonitorWebSource.TryParseValue(rawValue, out _));
        }

        /// <summary>
        /// Der Sensorbaum enthält neben dem Prozessor auch Mainboard-, Speicher-
        /// und Laufwerkssensoren. Die Auswahl darf deshalb nicht am Namen hängen.
        /// </summary>
        [Theory]
        [InlineData("/amdcpu/0/temperature/2", true)]
        [InlineData("/intelcpu/0/temperature/0", true)]
        [InlineData("/lpc/it8689e/0/temperature/1", false)]
        [InlineData("/nvme/1/temperature/0", false)]
        [InlineData("/gpu-nvidia/0/temperature/0", false)]
        public void IsCpuIdentifier_SelectsProcessorOnly(string identifier, bool expected)
        {
            Assert.Equal(expected, TemperatureSensorSelection.IsCpuIdentifier(identifier));
        }
    }
}
