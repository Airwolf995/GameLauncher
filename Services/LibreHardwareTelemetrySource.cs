using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace GameLauncher.Services
{
    internal sealed class LibreHardwareTelemetrySource : IHardwareTelemetrySource
    {
        private const string FailureInitialization = "Initialization";
        private const string FailureCpuTemperature = "CpuTemperature";
        private const string FailureGpuTemperature = "GpuTemperature";
        private const string FailureGpuLoad = "GpuLoad";
        private const string FailureGpuMemoryTotal = "GpuMemoryTotal";

        private static readonly TimeSpan SensorUpdateInterval = TimeSpan.FromSeconds(1);

        private readonly Computer _computer;
        private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private readonly SensorUpdateThrottle _updateThrottle;
        private bool _isAvailable;

        public LibreHardwareTelemetrySource()
            : this(null)
        {
        }

        internal LibreHardwareTelemetrySource(Func<DateTime>? utcNow)
        {
            _updateThrottle = new SensorUpdateThrottle(SensorUpdateInterval, utcNow);
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true
            };

            try
            {
                _computer.Open();
                _isAvailable = true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(FailureInitialization, "LibreHardwareMonitor konnte nicht initialisiert werden", ex);
                _isAvailable = false;
            }
        }

        public float? TryReadCpuTemperature()
        {
            return ExecuteRead(
                FailureCpuTemperature,
                "CPU-Temperatur konnte nicht per LibreHardwareMonitor gelesen werden",
                () =>
                {
                    float? packageTemperature = FindSensorValue(
                        hardware => hardware.HardwareType == HardwareType.Cpu,
                        sensor => sensor.SensorType == SensorType.Temperature
                            && string.Equals(sensor.Name, "CPU Package", StringComparison.OrdinalIgnoreCase));
                    if (packageTemperature.HasValue)
                    {
                        return packageTemperature;
                    }

                    return FindMaxSensorValue(
                        hardware => hardware.HardwareType == HardwareType.Cpu,
                        sensor => sensor.SensorType == SensorType.Temperature && IsCpuTemperatureSensor(sensor));
                });
        }

        public float? TryReadGpuTemperature()
        {
            return ExecuteRead(
                FailureGpuTemperature,
                "GPU-Temperatur konnte nicht per LibreHardwareMonitor gelesen werden",
                () => FindMaxSensorValue(
                    IsGpuHardware,
                    sensor => sensor.SensorType == SensorType.Temperature));
        }

        /// <summary>
        /// Liefert die Rechenlast der Grafikkarte, wie der Treiber sie meldet.
        ///
        /// Die Alternative ueber die Windows-Leistungsindikatoren der Kategorie
        /// "GPU Engine" ist prozessbezogen: dort existiert je Prozess eine eigene
        /// Instanz. Diese Quelle hier ist geraetebezogen und damit unabhaengig
        /// davon, welche Prozesse gerade laufen.
        /// </summary>
        public float? TryReadGpuLoad()
        {
            return ExecuteRead(
                FailureGpuLoad,
                "GPU-Auslastung konnte nicht per LibreHardwareMonitor gelesen werden",
                () =>
                {
                    float? coreLoad = FindGpuLoadValue(
                        sensor => string.Equals(sensor.Name, "GPU Core", StringComparison.OrdinalIgnoreCase));
                    if (coreLoad.HasValue)
                    {
                        return coreLoad;
                    }

                    return FindGpuLoadValue(IsGpuComputeLoadSensor);
                });
        }

        /// <summary>
        /// Wie <see cref="FindMaxSensorValue"/>, laesst aber den Wert 0 zu: eine
        /// Auslastung von null Prozent ist im Leerlauf die richtige Antwort und
        /// darf nicht als "kein Messwert" gelten.
        /// </summary>
        private float? FindGpuLoadValue(Func<ISensor, bool> sensorFilter)
        {
            float? maxValue = null;

            foreach (IHardware hardware in EnumerateHardware())
            {
                if (!IsGpuHardware(hardware))
                {
                    continue;
                }

                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Load || !sensorFilter(sensor))
                    {
                        continue;
                    }

                    if (sensor.Value is not float value ||
                        float.IsNaN(value) ||
                        float.IsInfinity(value) ||
                        value < 0)
                    {
                        continue;
                    }

                    if (!maxValue.HasValue || value > maxValue.Value)
                    {
                        maxValue = value;
                    }
                }
            }

            return maxValue;
        }

        /// <summary>
        /// Speicher- und Bus-Lastsensoren gehoeren nicht zur Rechenlast und
        /// wuerden den Wert verfaelschen.
        /// </summary>
        private static bool IsGpuComputeLoadSensor(ISensor sensor)
        {
            string name = sensor.Name;
            return name.IndexOf("memory", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("frame buffer", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("bus", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("video", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public float? TryReadGpuMemoryTotalGb()
        {
            return ExecuteRead(
                FailureGpuMemoryTotal,
                "GPU-Gesamtspeicher konnte nicht per LibreHardwareMonitor gelesen werden",
                () =>
                {
                    float? directMemoryTotal = FindMaxSensorValue(
                        IsGpuHardware,
                        sensor => IsGpuMemoryTotalSensor(sensor) || IsGpuMemoryDedicatedSensor(sensor));

                    return directMemoryTotal.HasValue && directMemoryTotal.Value > 0
                        ? directMemoryTotal
                        : null;
                });
        }

        private void UpdateAllHardware()
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                UpdateHardwareRecursive(hardware);
            }
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            hardware.Update();

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                UpdateHardwareRecursive(subHardware);
            }
        }

        private float? FindSensorValue(Func<IHardware, bool> hardwareFilter, Func<ISensor, bool> sensorFilter)
        {
            foreach (IHardware hardware in EnumerateHardware())
            {
                if (!hardwareFilter(hardware))
                {
                    continue;
                }

                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (!sensorFilter(sensor) || !TryGetValidValue(sensor, out float value))
                    {
                        continue;
                    }

                    return value;
                }
            }

            return null;
        }

        private float? FindMaxSensorValue(Func<IHardware, bool> hardwareFilter, Func<ISensor, bool> sensorFilter)
        {
            float? maxValue = null;

            foreach (IHardware hardware in EnumerateHardware())
            {
                if (!hardwareFilter(hardware))
                {
                    continue;
                }

                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (!sensorFilter(sensor) || !TryGetValidValue(sensor, out float value))
                    {
                        continue;
                    }

                    if (!maxValue.HasValue || value > maxValue.Value)
                    {
                        maxValue = value;
                    }
                }
            }

            return maxValue;
        }

        private IEnumerable<IHardware> EnumerateHardware()
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                foreach (IHardware nestedHardware in EnumerateHardwareRecursive(hardware))
                {
                    yield return nestedHardware;
                }
            }
        }

        private static IEnumerable<IHardware> EnumerateHardwareRecursive(IHardware hardware)
        {
            yield return hardware;

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                foreach (IHardware nestedHardware in EnumerateHardwareRecursive(subHardware))
                {
                    yield return nestedHardware;
                }
            }
        }

        private static bool IsGpuHardware(IHardware hardware)
        {
            return hardware.HardwareType == HardwareType.GpuAmd
                || hardware.HardwareType == HardwareType.GpuNvidia
                || hardware.HardwareType == HardwareType.GpuIntel;
        }

        private static bool IsCpuTemperatureSensor(ISensor sensor)
        {
            string name = sensor.Name.ToUpperInvariant();
            string identifier = sensor.Identifier.ToString().ToLowerInvariant();

            return identifier.Contains("/amdcpu/")
                || identifier.Contains("/intelcpu/")
                || name.Contains("CPU")
                || name.Contains("CORE")
                || name.Contains("PACKAGE")
                || name.Contains("TCTL")
                || name.Contains("TDIE")
                || name.Contains("TCCD");
        }

        private static bool IsGpuMemoryTotalSensor(ISensor sensor)
        {
            if (!IsDataSensor(sensor))
            {
                return false;
            }

            string name = sensor.Name.ToUpperInvariant();
            return name.Contains("MEMORY TOTAL")
                || name.Contains("GPU MEMORY TOTAL")
                || name.Contains("VRAM TOTAL");
        }

        private static bool IsGpuMemoryDedicatedSensor(ISensor sensor)
        {
            if (!IsDataSensor(sensor))
            {
                return false;
            }

            string name = sensor.Name.ToUpperInvariant();
            return name.Contains("D3D DEDICATED MEMORY")
                || name.Contains("DEDICATED MEMORY");
        }

        private static bool IsDataSensor(ISensor sensor)
        {
            return sensor.SensorType == SensorType.SmallData || sensor.SensorType == SensorType.Data;
        }

        private static bool TryGetValidValue(ISensor sensor, out float value)
        {
            value = 0;
            if (!sensor.Value.HasValue)
            {
                return false;
            }

            value = sensor.Value.Value;
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        }

        private float? ExecuteRead(string failureKey, string failureMessage, Func<float?> readOperation)
        {
            lock (_sync)
            {
                if (!_isAvailable)
                {
                    return null;
                }

                try
                {
                    // Ein Durchlauf fragt mehrere Einzelwerte ab; ohne diese
                    // Begrenzung loeste jeder davon ein vollstaendiges - und teures -
                    // Sensor-Update aus.
                    if (_updateThrottle.ShouldUpdate())
                    {
                        UpdateAllHardware();
                    }

                    return readOperation();
                }
                catch (Exception ex)
                {
                    LogFailureOnce(failureKey, failureMessage, ex);
                    return null;
                }
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

        public void Dispose()
        {
            _computer.Close();
        }
    }
}
