using System;

namespace GameLauncher.Services
{
    internal sealed class SensorTemperatureReader : IOptionalTemperatureReader
    {
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private bool _loggedUnavailable;

        public SensorTemperatureReader(IHardwareTelemetrySource hardwareTelemetrySource)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
        }

        /// <summary>
        /// Liefert die CPU-Temperatur oder <c>null</c>.
        ///
        /// Die Werte stammen aus der LibreHardwareMonitor-Anwendung. Läuft sie
        /// nicht, gibt es für den Prozessor keinen Ersatz: Windows selbst
        /// meldet keine Kerntemperatur.
        ///
        /// Bewusst ohne Rückfallebene auf die ACPI-Thermalzonen: die messen das
        /// Mainboard beziehungsweise das Gehäuse und liegen weit unter der
        /// Temperatur des Prozessors. Ein solcher Wert wäre schlechter als gar
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

        /// <summary>
        /// Ohne laufende LibreHardwareMonitor-Anwendung bleibt der Wert
        /// dauerhaft leer, ohne dass ein Fehler auftritt - der Hinweis im
        /// Protokoll benennt deshalb die Ursache.
        /// </summary>
        private void LogCpuTemperatureHintOnce()
        {
            if (_loggedUnavailable)
            {
                return;
            }

            _loggedUnavailable = true;
            Models.Logger.Warning(
                "CPU-Temperatur ist nicht verfügbar: es läuft keine "
                + "LibreHardwareMonitor-Anwendung. Sie muss installiert sein, laufen "
                + "und ihren Webserver aktiviert haben.");
        }
    }
}
