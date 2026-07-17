using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace GameLauncher.Services
{
    internal sealed class SensorTemperatureReader : IOptionalTemperatureReader, IDisposable
    {
        private const string FailureThermalCounterInit = "ThermalCounterInit";
        private const string FailureCpuAcpi = "CpuAcpi";
        private const string FailureGpuNvidiaSmi = "GpuNvidiaSmi";
        private const string ThermalZoneCategoryName = "Thermal Zone Information";
        private const string ThermalTemperatureCounterName = "Temperature";
        private const string ThermalHighPrecisionCounterName = "High Precision Temperature";

        private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private readonly List<PerformanceCounter> _thermalCounters = new();
        private readonly List<PerformanceCounter> _thermalHighPrecisionCounters = new();
        private readonly string? _nvidiaSmiPath;

        private bool _thermalCountersInitialized;
        private bool _thermalCountersAvailable = true;

        public SensorTemperatureReader(IHardwareTelemetrySource hardwareTelemetrySource)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
            _nvidiaSmiPath = NvidiaSmiHelper.ResolvePath();
        }

        public float? TryReadCpuTemperature()
        {
            float? hardwareMonitorTemperature = _hardwareTelemetrySource.TryReadCpuTemperature();
            if (hardwareMonitorTemperature.HasValue)
            {
                return hardwareMonitorTemperature;
            }

            float? thermalZoneTemperature = TryReadThermalZoneTemperature();
            if (thermalZoneTemperature.HasValue)
            {
                return thermalZoneTemperature;
            }

            return TryReadCpuTemperatureFromAcpiWmi();
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

        private float? TryReadThermalZoneTemperature()
        {
            EnsureThermalCountersInitialized();
            if (!_thermalCountersAvailable)
            {
                return null;
            }

            float? highPrecision = ReadBestThermalCounterValue(_thermalHighPrecisionCounters, allowDirectCelsius: true);
            if (highPrecision.HasValue)
            {
                return highPrecision;
            }

            return ReadBestThermalCounterValue(_thermalCounters, allowDirectCelsius: false);
        }

        private void EnsureThermalCountersInitialized()
        {
            if (_thermalCountersInitialized)
            {
                return;
            }

            _thermalCountersInitialized = true;

            try
            {
                if (!PerformanceCounterCategory.Exists(ThermalZoneCategoryName))
                {
                    _thermalCountersAvailable = false;
                    return;
                }

                var category = new PerformanceCounterCategory(ThermalZoneCategoryName);
                foreach (var instanceName in category.GetInstanceNames())
                {
                    TryAddThermalCounter(_thermalHighPrecisionCounters, instanceName, ThermalHighPrecisionCounterName);
                    TryAddThermalCounter(_thermalCounters, instanceName, ThermalTemperatureCounterName);
                }

                if (_thermalCounters.Count == 0 && _thermalHighPrecisionCounters.Count == 0)
                {
                    _thermalCountersAvailable = false;
                }
            }
            catch (Exception ex)
            {
                _thermalCountersAvailable = false;
                LogFailureOnce(
                    FailureThermalCounterInit,
                    "Thermal-Zone-Counter konnten nicht initialisiert werden",
                    ex);
            }
        }

        private static void TryAddThermalCounter(List<PerformanceCounter> target, string instanceName, string counterName)
        {
            try
            {
                var counter = new PerformanceCounter(ThermalZoneCategoryName, counterName, instanceName, readOnly: true);
                counter.NextValue();
                target.Add(counter);
            }
            catch
            {
            }
        }

        private static float? ReadBestThermalCounterValue(IEnumerable<PerformanceCounter> counters, bool allowDirectCelsius)
        {
            float? maxTemperature = null;

            foreach (var counter in counters)
            {
                try
                {
                    float rawValue = counter.NextValue();
                    float converted = ConvertRawTemperature(rawValue, allowDirectCelsius);
                    if (converted <= 0 || converted >= 150)
                    {
                        continue;
                    }

                    if (!maxTemperature.HasValue || converted > maxTemperature.Value)
                    {
                        maxTemperature = converted;
                    }
                }
                catch
                {
                }
            }

            return maxTemperature;
        }

        private float? TryReadCpuTemperatureFromAcpiWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\wmi",
                    "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject sensor in searcher.Get())
                {
                    if (sensor["CurrentTemperature"] == null)
                    {
                        continue;
                    }

                    float converted = ConvertRawTemperature(Convert.ToSingle(sensor["CurrentTemperature"]), allowDirectCelsius: true);
                    if (converted > 0 && converted < 150)
                    {
                        return converted;
                    }
                }
            }
            catch (Exception ex)
            {
                LogFailureOnce(
                    FailureCpuAcpi,
                    "CPU-Temperatur konnte nicht per ACPI-WMI gelesen werden",
                    ex);
            }

            return null;
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

        private static float ConvertRawTemperature(float rawValue, bool allowDirectCelsius)
        {
            float celsiusFromKelvinTenths = (rawValue / 10f) - 273.15f;
            if (celsiusFromKelvinTenths > 0 && celsiusFromKelvinTenths < 150)
            {
                return celsiusFromKelvinTenths;
            }

            if (allowDirectCelsius && rawValue > 15 && rawValue < 150)
            {
                return rawValue;
            }

            return -1;
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
            foreach (var counter in _thermalHighPrecisionCounters)
            {
                counter.Dispose();
            }
            _thermalHighPrecisionCounters.Clear();

            foreach (var counter in _thermalCounters)
            {
                counter.Dispose();
            }
            _thermalCounters.Clear();
        }
    }
}
