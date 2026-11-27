using System;

namespace GameLauncher.Services
{
    /// <summary>
    /// Regeln für die Auswahl der Temperatursensoren und die Prüfung ihrer Werte.
    /// </summary>
    internal static class TemperatureSensorSelection
    {
        public const string CpuPackageSensorName = "CPU Package";
        public const string GpuCoreSensorName = "GPU Core";

        public static bool IsCpuIdentifier(string sensorIdentifier)
        {
            string identifier = (sensorIdentifier ?? string.Empty).ToLowerInvariant();

            return identifier.Contains("/amdcpu/") || identifier.Contains("/intelcpu/");
        }

        /// <summary>
        /// Akzeptiert den ausdrücklich als Kerntemperatur bezeichneten Sensor.
        /// </summary>
        public static bool IsGpuCoreSensor(string sensorName)
        {
            return string.Equals(sensorName, GpuCoreSensorName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPlausibleTemperature(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0 && value < 150;
        }
    }
}
