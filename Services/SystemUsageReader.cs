using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GameLauncher.Services
{
    internal sealed class SystemUsageReader : ISystemUsageReader
    {
        private readonly GpuTelemetryReader? _gpuReader;
        private readonly Func<float?> _readCpuUsage;
        private readonly Func<(float? usedGb, float? totalGb, float? loadPercent)> _readMemoryStats;
        private readonly Func<GpuReading> _readGpu;
        private readonly Action<string, Exception> _logError;
        private readonly HashSet<string> _loggedReadErrors = new(StringComparer.Ordinal);

        private ulong? _lastCpuIdleTime;
        private ulong? _lastCpuKernelTime;
        private ulong? _lastCpuUserTime;

        public SystemUsageReader(IHardwareTelemetrySource hardwareTelemetrySource)
        {
            _gpuReader = new GpuTelemetryReader(hardwareTelemetrySource);
            _readGpu = _gpuReader.Read;
            _readCpuUsage = ReadCpuUsage;
            _readMemoryStats = ReadMemoryStats;
            _logError = Models.Logger.Error;
        }

        internal SystemUsageReader(
            Func<float?> readCpuUsage,
            Func<(float? usedGb, float? totalGb, float? loadPercent)> readMemoryStats,
            Func<GpuReading> readGpu,
            Action<string, Exception> logError)
        {
            _readCpuUsage = readCpuUsage;
            _readMemoryStats = readMemoryStats;
            _readGpu = readGpu;
            _logError = logError;
        }

        public HardwareStatsSnapshot ReadSnapshot()
        {
            float? cpuUsage = TryReadCpuUsage();
            var memory = TryReadMemoryStats();
            var gpu = TryReadGpu();
            const double bytesPerGiB = 1024d * 1024 * 1024;
            float? vramLoad = gpu.UsedBytes is ulong used && gpu.TotalBytes is > 0
                ? (float)Math.Min(100, 100d * used / gpu.TotalBytes.Value)
                : null;

            return new HardwareStatsSnapshot(
                CpuTemp: null,
                CpuUsage: cpuUsage,
                GpuTemp: gpu.Temperature,
                GpuUsage: gpu.Usage,
                RamUsedGb: memory.usedGb,
                RamTotalGb: memory.totalGb,
                RamLoad: memory.loadPercent,
                VramUsedGb: (float?)(gpu.UsedBytes / bytesPerGiB),
                VramTotalGb: (float?)(gpu.TotalBytes / bytesPerGiB),
                VramLoad: vramLoad);
        }

        private float? TryReadCpuUsage()
        {
            try
            {
                return _readCpuUsage();
            }
            catch (Exception ex)
            {
                LogReadErrorOnce("CpuUsage", "HardwareMonitor: CPU-Auslastung konnte nicht gelesen werden", ex);
                return null;
            }
        }

        private (float? usedGb, float? totalGb, float? loadPercent) TryReadMemoryStats()
        {
            try
            {
                return _readMemoryStats();
            }
            catch (Exception ex)
            {
                LogReadErrorOnce("Memory", "HardwareMonitor: RAM-Auslastung konnte nicht gelesen werden", ex);
                return (null, null, null);
            }
        }

        private GpuReading TryReadGpu()
        {
            try
            {
                return _readGpu();
            }
            catch (Exception ex)
            {
                LogReadErrorOnce("Gpu", "HardwareMonitor: GPU-Werte konnten nicht gelesen werden", ex);
                return default;
            }
        }

        private void LogReadErrorOnce(string errorKey, string message, Exception ex)
        {
            if (!_loggedReadErrors.Add(errorKey))
            {
                return;
            }

            _logError(message, ex);
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

        public void Dispose() => _gpuReader?.Dispose();

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
