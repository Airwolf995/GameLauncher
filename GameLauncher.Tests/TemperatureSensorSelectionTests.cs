using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class TemperatureSensorSelectionTests
    {
        /// <summary>
        /// Der Ausloeser: Hot Spot und Memory Junction liegen deutlich ueber der
        /// Kerntemperatur. Wurden sie mitgezaehlt, zeigte das Overlay einen zu
        /// hohen Wert.
        /// </summary>
        [Theory]
        [InlineData("GPU Hot Spot")]
        [InlineData("GPU Hotspot")]
        [InlineData("GPU Memory Junction")]
        [InlineData("GPU VRM")]
        [InlineData("GPU")]
        public void IsGpuCoreSensor_ExcludesSensorsAboveCoreTemperature(string sensorName)
        {
            Assert.False(TemperatureSensorSelection.IsGpuCoreSensor(sensorName));
        }

        [Fact]
        public void IsGpuCoreSensor_AcceptsCoreTemperature()
        {
            Assert.True(TemperatureSensorSelection.IsGpuCoreSensor("GPU Core"));
        }

        /// <summary>
        /// Ohne Sensortreiber meldet der Prozessor 0 statt eines Messwerts. Ein
        /// solcher Wert darf nicht als Temperatur durchgehen.
        /// </summary>
        [Theory]
        [InlineData(0f)]
        [InlineData(-5f)]
        [InlineData(200f)]
        [InlineData(float.NaN)]
        public void IsPlausibleTemperature_RejectsUnusableValues(float value)
        {
            Assert.False(TemperatureSensorSelection.IsPlausibleTemperature(value));
        }

        [Theory]
        [InlineData(49f)]
        [InlineData(68f)]
        public void IsPlausibleTemperature_AcceptsRealisticValues(float value)
        {
            Assert.True(TemperatureSensorSelection.IsPlausibleTemperature(value));
        }
    }
}
