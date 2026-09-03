using System;
using System.Collections.Generic;

namespace GameLauncher.Services
{
    internal sealed class SensorTemperatureReader : IOptionalTemperatureReader
    {
        private const string FailureGpuNvidiaSmi = "GpuNvidiaSmi";
        private const string HintCpuTemperatureUnavailable = "CpuTemperatureUnavailable";

        private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private readonly string? _nvidiaSmiPath;

        public SensorTemperatureReader(IHardwareTelemetrySource hardwareTelemetrySource)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
            _nvidiaSmiPath = NvidiaSmiHelper.ResolvePath();
        }

        /// <summary>
        /// Liefert die CPU-Temperatur oder <c>null</c>.
        ///
        /// Die Werte stammen aus der LibreHardwareMonitor-Anwendung. Laeuft sie
        /// nicht, gibt es fuer den Prozessor keinen Ersatz: Windows selbst
        /// meldet keine Kerntemperatur.
        ///
        /// Bewusst ohne Rueckfallebene auf die ACPI-Thermalzonen: die messen das
        /// Mainboard beziehungsweise das Gehaeuse und liegen weit unter der
        /// Temperatur des Prozessors. Ein solcher Wert waere schlechter als gar
        /// keiner, weil er in der Anzeige nicht von einer echten Messung zu
        /// unterscheiden waere.
        /// </summary>
        public float? TryReadCpuTemperature()
        {
            float? hardwareMonitorTemperature = _hardwareTelemetrySource.TryReadCpuTemperature();
            if (hardwareMonitorTemperature.HasValue)
            {
                return hardwareMonitorTemperature;
            }

            LogCpuTemperatureHintOnce();
            return null;
        }

        public float? TryReadGpuTemperature()
        {
            float? hardwareMonitorTemperature = _hardwareTelemetrySource.TryReadGpuTemperature();
            if (hardwareMonitorTemperature.HasValue)
            {
                return hardwareMonitorTemperature;
            }

            return TryReadGpuTemperatureFromNvidiaSmi();
        }

        /// <summary>
        /// Ohne laufende LibreHardwareMonitor-Anwendung bleibt der Wert
        /// dauerhaft leer, ohne dass ein Fehler auftritt - der Hinweis im
        /// Protokoll benennt deshalb die Ursache.
        /// </summary>
        private void LogCpuTemperatureHintOnce()
        {
            if (!_loggedFailures.Add(HintCpuTemperatureUnavailable))
            {
                return;
            }

            Models.Logger.Warning(
                "CPU-Temperatur ist nicht verfuegbar: es laeuft keine "
                + "LibreHardwareMonitor-Anwendung. Sie muss installiert sein, laufen "
                + "und ihren Webserver aktiviert haben.");
        }

        private float? TryReadGpuTemperatureFromNvidiaSmi()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_nvidiaSmiPath))
                {
                    return null;
                }

                string? firstLine = NvidiaSmiHelper.TryQueryFirstLine(_nvidiaSmiPath, "--query-gpu=temperature.gpu");
                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return null;
                }

                return float.TryParse(firstLine, out float temperature) && temperature > 0 && temperature < 150
                    ? temperature
                    : null;
            }
            catch (Exception ex)
            {
                LogFailureOnce(
                    FailureGpuNvidiaSmi,
                    "GPU-Temperatur konnte nicht per nvidia-smi gelesen werden",
                    ex);
                return null;
            }
        }

        private void LogFailureOnce(string failureKey, string message, Exception ex)
        {
            if (!_loggedFailures.Add(failureKey))
            {
                return;
            }

            Models.Logger.Error(message, ex);
        }
    }
}
