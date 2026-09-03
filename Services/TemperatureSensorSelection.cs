using System;

namespace GameLauncher.Services
{
    /// <summary>
    /// Regeln fuer die Auswahl der Temperatursensoren.
    ///
    /// Die Werte koennen aus der eingebetteten Bibliothek oder aus einer
    /// laufenden LibreHardwareMonitor-Anwendung stammen. Beide Quellen liefern
    /// dieselben Sensornamen, deshalb liegt die Auswahl hier gemeinsam - sonst
    /// zeigte der Launcher je nach Quelle unterschiedliche Temperaturen.
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

        public static bool IsGpuIdentifier(string sensorIdentifier)
        {
            string identifier = (sensorIdentifier ?? string.Empty).ToLowerInvariant();

            return identifier.Contains("/gpu-nvidia/")
                || identifier.Contains("/gpu-amd/")
                || identifier.Contains("/gpu-intel/")
                || identifier.Contains("/atigpu/")
                || identifier.Contains("/nvidiagpu/");
        }

        /// <summary>
        /// Blendet die Sensoren aus, die systematisch ueber der Kerntemperatur
        /// liegen und daher nicht als Ersatz fuer sie taugen.
        /// </summary>
        public static bool IsGpuCoreSensor(string sensorName)
        {
            string name = sensorName ?? string.Empty;

            return name.IndexOf("hot spot", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("hotspot", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("junction", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("memory", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("vrm", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public static bool IsPlausibleTemperature(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0 && value < 150;
        }
    }
}
