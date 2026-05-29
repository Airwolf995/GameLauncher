using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace GameLauncher.Services
{
    public class HardwareMonitorService : IDisposable
    {
        private static readonly string[] CpuTempPrimaryNames = { "Package", "Average", "Tdie" };
        private static readonly string[] CpuTempFallbackNames = { "Tctl", "Core" };
        private static readonly string[] CpuLoadTotalNames = { "Total", "CPU Total", "Overall" };
        private static readonly string[] CpuLoadCoreNames = { "Core", "CPU" };

        private static readonly string[] GpuTempNames = { "Core", "GPU", "Hot Spot" };
        private static readonly string[] GpuLoadNames = { "Core", "Load" };
        private static readonly string[] GpuMemLoadNames = { "Memory" };
        private static readonly string[] VramUsedNames = { "Memory Used", "GPU Memory Used", "VRAM Used", "FB Usage" };
        private static readonly string[] VramTotalNames = { "Memory Total", "GPU Memory Total", "VRAM Total", "Dedicated Memory" };
        private static readonly string[] VramFreeNames = { "Memory Free", "Memory Available", "VRAM Free" };

        private static readonly string[] RamLoadNames = { "Memory" };
        private static readonly string[] RamUsedNames = { "Used Memory", "Memory Used" };
        private static readonly string[] RamAvailableNames = { "Available Memory", "Memory Available" };
        private static readonly string[] RamTotalNames = { "Total Memory" };

        private readonly Computer _computer;
        private readonly UpdateVisitor _updateVisitor = new UpdateVisitor();

        // --- Sensor-Cache ---
        private bool _sensorsResolved;
        private readonly List<IHardware> _trackedHardware = new();

        // CPU
        private ISensor? _cpuTempSensor;
        private ISensor? _cpuLoadTotalSensor;
        private readonly List<ISensor> _cpuCoreLoadSensors = new();

        // GPU
        private ISensor? _gpuTempSensor;
        private ISensor? _gpuLoadSensor;
        private ISensor? _gpuMemLoadSensor;
        private ISensor? _vramUsedSensor;
        private ISensor? _vramTotalSensor;
        private ISensor? _vramFreeSensor;
        private bool _vramUsedIsSmallData;
        private bool _vramTotalIsSmallData;
        private bool _vramFreeIsSmallData;

        // RAM
        private ISensor? _ramLoadSensor;
        private ISensor? _ramUsedSensor;
        private ISensor? _ramAvailableSensor;
        private ISensor? _ramTotalSensor;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true, // Behalten: CPU-Temp-Fallback
                // Deaktiviert: Storage, Controller, PSU — werden nicht benötigt
            };
            _computer.Open();
            LogAvailableHardware();
        }

        private void LogAvailableHardware()
        {
            try 
            {
                foreach (var hardware in _computer.Hardware)
                {
                    Models.Logger.Log($"Hardware detected: {hardware.Name} ({hardware.HardwareType})");
                }
            }
            catch { }
        }

        public (float? cpuTemp, float? cpuUsage, float? gpuTemp, float? gpuUsage,
                float? ramUsedGb, float? ramTotalGb, float? ramLoad,
                float? vramUsedGb, float? vramTotalGb, float? vramLoad) GetHardwareStats()
        {
            if (!_sensorsResolved)
            {
                ResolveSensors();
            }

            // Nur die relevante Hardware aktualisieren (kein Storage/PSU/Controller mehr)
            foreach (var hw in _trackedHardware)
            {
                hw.Update();
            }

            // Direkte Reads von gecachten Sensoren — kein Iterieren/ContainsAny mehr
            float? cpuTemp = _cpuTempSensor?.Value;
            float? cpuUsage = _cpuLoadTotalSensor?.Value;

            if (!cpuUsage.HasValue && _cpuCoreLoadSensors.Count > 0)
            {
                float sum = 0;
                int count = 0;
                foreach (var s in _cpuCoreLoadSensors)
                {
                    if (s.Value.HasValue && s.Value.Value >= 0)
                    {
                        sum += s.Value.Value;
                        count++;
                    }
                }
                if (count > 0) cpuUsage = sum / count;
            }

            float? gpuTemp = _gpuTempSensor?.Value;
            float? gpuUsage = _gpuLoadSensor?.Value;
            float? vramLoad = _gpuMemLoadSensor?.Value;

            float? vramUsedGb = ReadVramValue(_vramUsedSensor, _vramUsedIsSmallData);
            float? vramTotalGb = ReadVramValue(_vramTotalSensor, _vramTotalIsSmallData);
            float? vramFreeGb = ReadVramValue(_vramFreeSensor, _vramFreeIsSmallData);

            float? ramLoad = _ramLoadSensor?.Value;
            float? ramUsedGb = _ramUsedSensor?.Value;
            float? ramAvailableGb = _ramAvailableSensor?.Value;
            float? ramTotalGb = _ramTotalSensor?.Value;

            // Fallback-Berechnungen (wie bisher)
            if (!ramTotalGb.HasValue && ramUsedGb.HasValue && ramAvailableGb.HasValue)
            {
                ramTotalGb = ramUsedGb.Value + ramAvailableGb.Value;
            }

            if (!vramTotalGb.HasValue && vramUsedGb.HasValue && vramFreeGb.HasValue)
            {
                vramTotalGb = vramUsedGb.Value + vramFreeGb.Value;
            }

            return (cpuTemp, cpuUsage, gpuTemp, gpuUsage,
                    ramUsedGb, ramTotalGb, ramLoad,
                    vramUsedGb, vramTotalGb, vramLoad);
        }

        /// <summary>
        /// Asynchrone Abfrage der Hardware-Statistiken, um UI-Ruckler zu vermeiden.
        /// </summary>
        public async Task<(float? cpuTemp, float? cpuUsage, float? gpuTemp, float? gpuUsage,
                float? ramUsedGb, float? ramTotalGb, float? ramLoad,
                float? vramUsedGb, float? vramTotalGb, float? vramLoad)> GetHardwareStatsAsync()
        {
            return await Task.Run(() => GetHardwareStats());
        }

        /// <summary>
        /// Einmalige Sensor-Erkennung: Sucht die passenden Sensoren und cacht deren Referenzen.
        /// Wird nur beim ersten GetHardwareStats()-Aufruf ausgeführt.
        /// </summary>
        private void ResolveSensors()
        {
            // Voller Update damit alle Sensoren populiert werden
            _computer.Accept(_updateVisitor);

            ISensor? cpuTempPrimary = null;
            ISensor? cpuTempFallback = null;
            ISensor? cpuTempMb = null;
            var allCpuTempSensors = new List<ISensor>();

            foreach (IHardware hardware in EnumerateHardware())
            {
                bool isRelevant = false;

                foreach (ISensor sensor in hardware.Sensors)
                {
                    // --- CPU ---
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        isRelevant = true;
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (!sensor.Value.HasValue) continue;
                            
                            // Für die Max-Fallback-Logik nur Sensoren mit echtem Wert > 0
                            if (sensor.Value.Value > 0)
                                allCpuTempSensors.Add(sensor);

                            // Sensor trotzdem cachen — Zen 4 CPUs liefern beim ersten Update oft 0,
                            // haben aber bei späteren Reads korrekte Werte.
                            if (ContainsAny(sensor.Name, CpuTempPrimaryNames))
                                cpuTempPrimary = sensor;
                            else if (cpuTempFallback == null && ContainsAny(sensor.Name, CpuTempFallbackNames))
                                cpuTempFallback = sensor;
                        }
                        else if (sensor.SensorType == SensorType.Load)
                        {
                            if (!sensor.Value.HasValue || sensor.Value.Value < 0) continue;
                            if (ContainsAny(sensor.Name, CpuLoadTotalNames))
                                _cpuLoadTotalSensor = sensor;
                            else if (ContainsAny(sensor.Name, CpuLoadCoreNames))
                                _cpuCoreLoadSensors.Add(sensor);
                        }
                    }

                    // --- Motherboard (CPU-Temp-Fallback) ---
                    if (hardware.HardwareType == HardwareType.Motherboard)
                    {
                        isRelevant = true;
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (!sensor.Value.HasValue || sensor.Value.Value <= 0) continue;
                            if (sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || 
                                sensor.Name.Contains("Processor", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTempMb = sensor;
                            }
                        }
                    }

                    // --- GPU ---
                    if (hardware.HardwareType == HardwareType.GpuNvidia || 
                        hardware.HardwareType == HardwareType.GpuAmd || 
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        isRelevant = true;
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (sensor.Value.HasValue && sensor.Value.Value > 0 &&
                                ContainsAny(sensor.Name, GpuTempNames))
                            {
                                _gpuTempSensor = sensor;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Load)
                        {
                            if (ContainsAny(sensor.Name, GpuLoadNames))
                                _gpuLoadSensor = sensor;
                            else if (ContainsAny(sensor.Name, GpuMemLoadNames))
                                _gpuMemLoadSensor = sensor;
                        }
                        else if (sensor.SensorType == SensorType.Data || sensor.SensorType == SensorType.SmallData)
                        {
                            bool isSmall = sensor.SensorType == SensorType.SmallData;
                            if (ContainsAny(sensor.Name, VramUsedNames))
                            {
                                _vramUsedSensor = sensor;
                                _vramUsedIsSmallData = isSmall;
                            }
                            else if (ContainsAny(sensor.Name, VramTotalNames))
                            {
                                _vramTotalSensor = sensor;
                                _vramTotalIsSmallData = isSmall;
                            }
                            else if (ContainsAny(sensor.Name, VramFreeNames))
                            {
                                _vramFreeSensor = sensor;
                                _vramFreeIsSmallData = isSmall;
                            }
                        }
                    }

                    // --- RAM ---
                    if (hardware.HardwareType == HardwareType.Memory)
                    {
                        isRelevant = true;
                        if (sensor.SensorType == SensorType.Load && ContainsAny(sensor.Name, RamLoadNames))
                        {
                            _ramLoadSensor = sensor;
                        }
                        else if (sensor.SensorType == SensorType.Data)
                        {
                            if (ContainsAny(sensor.Name, RamUsedNames))
                                _ramUsedSensor = sensor;
                            else if (ContainsAny(sensor.Name, RamAvailableNames))
                                _ramAvailableSensor = sensor;
                            else if (ContainsAny(sensor.Name, RamTotalNames))
                                _ramTotalSensor = sensor;
                        }
                    }
                }

                if (isRelevant && !_trackedHardware.Contains(hardware))
                {
                    _trackedHardware.Add(hardware);
                }
            }

            // CPU-Temp-Priorität auflösen: Primary > Fallback > Motherboard > Max
            _cpuTempSensor = cpuTempPrimary ?? cpuTempFallback ?? cpuTempMb;
            if (_cpuTempSensor == null && allCpuTempSensors.Count > 0)
            {
                _cpuTempSensor = allCpuTempSensors.OrderByDescending(s => s.Value ?? 0).First();
            }

            _sensorsResolved = true;

            Models.Logger.Log($"HardwareMonitor: sensor cache initialized. " +
                $"Tracking {_trackedHardware.Count} hardware device(s). " +
                $"CPU temp={_cpuTempSensor?.Name ?? "n/a"}, " +
                $"CPU load={(_cpuLoadTotalSensor != null ? "Total" : $"{_cpuCoreLoadSensors.Count} Cores")}, " +
                $"GPU temp={_gpuTempSensor?.Name ?? "n/a"}, " +
                $"GPU load={_gpuLoadSensor?.Name ?? "n/a"}");
        }

        private static float? ReadVramValue(ISensor? sensor, bool isSmallData)
        {
            if (sensor?.Value == null) return null;
            return isSmallData ? sensor.Value.Value / 1024f : sensor.Value;
        }

        private IEnumerable<IHardware> EnumerateHardware()
        {
            foreach (var hardware in _computer.Hardware)
            {
                yield return hardware;

                foreach (var sub in hardware.SubHardware)
                {
                    yield return sub;
                }
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                {
                    subHardware.Accept(this);
                }
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        private static bool ContainsAny(string text, string[] needles)
        {
            foreach (var needle in needles)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            _computer.Close();
        }
    }
}
