using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace GameLauncher.Services
{
    internal sealed class SystemUsageReader : IDisposable
    {
        private const string GpuEngineCategoryName = "GPU Engine";
        private const string GpuAdapterMemoryCategoryName = "GPU Adapter Memory";
        private const string GpuUtilizationCounterName = "Utilization Percentage";
        private const string GpuDedicatedUsageCounterName = "Dedicated Usage";

        private readonly List<PerformanceCounter> _gpuEngineCounters = new();
        private readonly List<PerformanceCounter> _gpuMemoryCounters = new();
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private readonly string? _nvidiaSmiPath;

        private bool _gpuCountersInitialized;
        private bool _gpuCountersAvailable = true;
        private bool _gpuMemoryCountersAvailable = true;
        private bool _gpuMemoryTotalResolved;
        private float? _cachedGpuMemoryTotalGb;

        private ulong? _lastCpuIdleTime;
        private ulong? _lastCpuKernelTime;
        private ulong? _lastCpuUserTime;

        public SystemUsageReader(IHardwareTelemetrySource hardwareTelemetrySource)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
            _nvidiaSmiPath = NvidiaSmiHelper.ResolvePath();
        }

        public HardwareStatsSnapshot ReadSnapshot()
        {
            float? cpuUsage = ReadCpuUsage();
            var memory = ReadMemoryStats();
            float? gpuUsage = ReadGpuUsage();
            var vram = ReadVramStats();

            return new HardwareStatsSnapshot(
                CpuTemp: null,
                CpuUsage: cpuUsage,
                GpuTemp: null,
                GpuUsage: gpuUsage,
                RamUsedGb: memory.usedGb,
                RamTotalGb: memory.totalGb,
                RamLoad: memory.loadPercent,
                VramUsedGb: vram.usedGb,
                VramTotalGb: vram.totalGb,
                VramLoad: vram.loadPercent);
        }

        private float? ReadCpuUsage()
        {
            if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            {
                return null;
            }

            ulong idle = idleTime.ToUInt64();
            ulong kernel = kernelTime.ToUInt64();
            ulong user = userTime.ToUInt64();

            if (!_lastCpuIdleTime.HasValue || !_lastCpuKernelTime.HasValue || !_lastCpuUserTime.HasValue)
            {
                _lastCpuIdleTime = idle;
                _lastCpuKernelTime = kernel;
                _lastCpuUserTime = user;
                return null;
            }

            ulong idleDelta = idle - _lastCpuIdleTime.Value;
            ulong kernelDelta = kernel - _lastCpuKernelTime.Value;
            ulong userDelta = user - _lastCpuUserTime.Value;

            _lastCpuIdleTime = idle;
            _lastCpuKernelTime = kernel;
            _lastCpuUserTime = user;

            ulong total = kernelDelta + userDelta;
            if (total == 0)
            {
                return null;
            }

            double busy = Math.Max(0, total - idleDelta);
            return (float)Math.Clamp((busy / total) * 100.0, 0.0, 100.0);
        }

        private static (float? usedGb, float? totalGb, float? loadPercent) ReadMemoryStats()
        {
            var memoryStatus = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(memoryStatus))
            {
                return (null, null, null);
            }

            double totalBytes = memoryStatus.ullTotalPhys;
            double availableBytes = memoryStatus.ullAvailPhys;
            double usedBytes = Math.Max(0, totalBytes - availableBytes);

            float totalGb = (float)(totalBytes / (1024d * 1024d * 1024d));
            float usedGb = (float)(usedBytes / (1024d * 1024d * 1024d));
            float loadPercent = memoryStatus.dwMemoryLoad;

            return (usedGb, totalGb, loadPercent);
        }

        private float? ReadGpuUsage()
        {
            EnsureGpuCountersInitialized();
            if (!_gpuCountersAvailable || _gpuEngineCounters.Count == 0)
            {
                return null;
            }

            double maxUsage = 0;
            bool hasValue = false;

            foreach (var counter in _gpuEngineCounters)
            {
                try
                {
                    float value = counter.NextValue();
                    if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                    {
                        continue;
                    }

                    hasValue = true;
                    if (value > maxUsage)
                    {
                        maxUsage = value;
                    }
                }
                catch
                {
                }
            }

            return hasValue ? (float)Math.Clamp(maxUsage, 0.0, 100.0) : null;
        }

        private (float? usedGb, float? totalGb, float? loadPercent) ReadVramStats()
        {
            EnsureGpuCountersInitialized();
            if (!_gpuMemoryCountersAvailable || _gpuMemoryCounters.Count == 0)
            {
                return (null, null, null);
            }

            double dedicatedBytes = 0;
            bool hasValue = false;

            foreach (var counter in _gpuMemoryCounters)
            {
                try
                {
                    float value = counter.NextValue();
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                    {
                        continue;
                    }

                    dedicatedBytes += value;
                    hasValue = true;
                }
                catch
                {
                }
            }

            if (!hasValue)
            {
                return (null, null, null);
            }

            float usedGb = (float)(dedicatedBytes / (1024d * 1024d * 1024d));
            float? totalGb = ReadGpuMemoryTotalGb();
            float? loadPercent = null;
            if (totalGb.HasValue && totalGb.Value > 0)
            {
                loadPercent = (float)Math.Clamp((usedGb / totalGb.Value) * 100f, 0f, 100f);
            }

            return (usedGb, totalGb, loadPercent);
        }

        private void EnsureGpuCountersInitialized()
        {
            if (_gpuCountersInitialized)
            {
                return;
            }

            _gpuCountersInitialized = true;
            InitializeGpuUsageCounters();
            InitializeGpuMemoryCounters();
        }

        private void InitializeGpuUsageCounters()
        {
            _gpuCountersAvailable = InitializeCounters(
                target: _gpuEngineCounters,
                categoryName: GpuEngineCategoryName,
                counterName: GpuUtilizationCounterName,
                instanceFilter: ShouldUseGpuEngineInstance,
                unavailableMessage: "HardwareMonitor: GPU-Engine-Counter sind auf diesem System nicht verfügbar.",
                emptyMessage: "HardwareMonitor: keine geeigneten GPU-Engine-Counter gefunden.",
                errorMessage: "GPU-Engine-Counter konnten nicht initialisiert werden");
        }

        private void InitializeGpuMemoryCounters()
        {
            _gpuMemoryCountersAvailable = InitializeCounters(
                target: _gpuMemoryCounters,
                categoryName: GpuAdapterMemoryCategoryName,
                counterName: GpuDedicatedUsageCounterName,
                instanceFilter: ShouldUseGpuMemoryInstance,
                unavailableMessage: "HardwareMonitor: GPU-Speicher-Counter sind auf diesem System nicht verfügbar.",
                emptyMessage: "HardwareMonitor: keine geeigneten GPU-Speicher-Counter gefunden.",
                errorMessage: "GPU-Speicher-Counter konnten nicht initialisiert werden");
        }

        private static bool InitializeCounters(
            List<PerformanceCounter> target,
            string categoryName,
            string counterName,
            Func<string, bool> instanceFilter,
            string unavailableMessage,
            string emptyMessage,
            string errorMessage)
        {
            try
            {
                if (!PerformanceCounterCategory.Exists(categoryName))
                {
                    Models.Logger.Log(unavailableMessage);
                    return false;
                }

                var category = new PerformanceCounterCategory(categoryName);
                foreach (var instanceName in category.GetInstanceNames())
                {
                    if (!instanceFilter(instanceName))
                    {
                        continue;
                    }

                    TryAddCounter(target, categoryName, counterName, instanceName);
                }

                if (target.Count > 0)
                {
                    return true;
                }

                Models.Logger.Log(emptyMessage);
                return false;
            }
            catch (Exception ex)
            {
                Models.Logger.Error(errorMessage, ex);
                return false;
            }
        }

        private static void TryAddCounter(
            List<PerformanceCounter> target,
            string categoryName,
            string counterName,
            string instanceName)
        {
            try
            {
                var counter = new PerformanceCounter(categoryName, counterName, instanceName, readOnly: true);
                counter.NextValue();
                target.Add(counter);
            }
            catch
            {
            }
        }

        private static bool ShouldUseGpuEngineInstance(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return false;
            }

            string normalized = instanceName.ToLowerInvariant();
            if (!normalized.Contains("engtype"))
            {
                return false;
            }

            return normalized.Contains("engtype_3d")
                || normalized.Contains("engtype_compute")
                || normalized.Contains("engtype_cuda")
                || normalized.Contains("engtype_copy")
                || normalized.Contains("engtype_video");
        }

        private static bool ShouldUseGpuMemoryInstance(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return false;
            }

            string normalized = instanceName.ToLowerInvariant();
            return normalized.Contains("luid");
        }

        private float? ReadGpuMemoryTotalGb()
        {
            if (_gpuMemoryTotalResolved)
            {
                return _cachedGpuMemoryTotalGb;
            }

            _gpuMemoryTotalResolved = true;

            float? nvidiaSmiTotal = ReadGpuMemoryTotalGbFromNvidiaSmi();
            if (nvidiaSmiTotal.HasValue && nvidiaSmiTotal.Value > 0)
            {
                _cachedGpuMemoryTotalGb = nvidiaSmiTotal;
                return _cachedGpuMemoryTotalGb;
            }

            float? hardwareMonitorTotal = _hardwareTelemetrySource.TryReadGpuMemoryTotalGb();
            if (hardwareMonitorTotal.HasValue && hardwareMonitorTotal.Value > 0)
            {
                _cachedGpuMemoryTotalGb = hardwareMonitorTotal;
                return _cachedGpuMemoryTotalGb;
            }

            float? wmiTotal = ReadGpuMemoryTotalGbFromWmi();
            if (wmiTotal.HasValue && wmiTotal.Value > 0)
            {
                _cachedGpuMemoryTotalGb = wmiTotal;
                return _cachedGpuMemoryTotalGb;
            }

            return _cachedGpuMemoryTotalGb;
        }

        private float? ReadGpuMemoryTotalGbFromNvidiaSmi()
        {
            if (string.IsNullOrWhiteSpace(_nvidiaSmiPath))
            {
                return null;
            }

            try
            {
                string? firstLine = NvidiaSmiHelper.TryQueryFirstLine(_nvidiaSmiPath, "--query-gpu=memory.total");
                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return null;
                }

                if (!float.TryParse(firstLine, out float totalMiB) || totalMiB <= 0)
                {
                    return null;
                }

                return totalMiB / 1024f;
            }
            catch
            {
                return null;
            }
        }

        private static float? ReadGpuMemoryTotalGbFromWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\cimv2",
                    "SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController");

                ulong maxAdapterRamBytes = 0;
                foreach (ManagementObject controller in searcher.Get())
                {
                    string name = controller["Name"]?.ToString() ?? string.Empty;
                    string pnpDeviceId = controller["PNPDeviceID"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(pnpDeviceId))
                    {
                        continue;
                    }

                    string normalizedName = name.ToUpperInvariant();
                    string normalizedPnpDeviceId = pnpDeviceId.ToUpperInvariant();
                    if (normalizedName.Contains("MICROSOFT BASIC DISPLAY")
                        || normalizedPnpDeviceId.Contains("DISPLAY\\BASICDISPLAY"))
                    {
                        continue;
                    }

                    if (controller["AdapterRAM"] == null)
                    {
                        continue;
                    }

                    ulong adapterRamBytes = Convert.ToUInt64(controller["AdapterRAM"]);
                    if (adapterRamBytes > maxAdapterRamBytes)
                    {
                        maxAdapterRamBytes = adapterRamBytes;
                    }
                }

                if (maxAdapterRamBytes == 0)
                {
                    return null;
                }

                return (float)(maxAdapterRamBytes / (1024d * 1024d * 1024d));
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var counter in _gpuEngineCounters)
            {
                counter.Dispose();
            }
            _gpuEngineCounters.Clear();

            foreach (var counter in _gpuMemoryCounters)
            {
                counter.Dispose();
            }
            _gpuMemoryCounters.Clear();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct FileTime
        {
            public readonly uint LowDateTime;
            public readonly uint HighDateTime;

            public ulong ToUInt64()
            {
                return ((ulong)HighDateTime << 32) | LowDateTime;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatusEx
        {
            public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
