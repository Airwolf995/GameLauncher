using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameLauncher.Services
{
    public class HardwareMonitorService : IDisposable
    {
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private readonly object _sync = new();
        private readonly ISystemUsageReader _systemUsageReader;
        private readonly IOptionalTemperatureReader _temperatureReader;
        private readonly Action<string, Exception> _logError;
        private readonly HashSet<string> _loggedReadErrors = new(StringComparer.Ordinal);
        private bool _disposed;

        public HardwareMonitorService()
            : this(new LibreHardwareTelemetrySource())
        {
        }

        internal HardwareMonitorService(IHardwareTelemetrySource hardwareTelemetrySource)
            : this(
                hardwareTelemetrySource,
                new SystemUsageReader(hardwareTelemetrySource),
                new SensorTemperatureReader(hardwareTelemetrySource),
                Models.Logger.Error)
        {
        }

        internal HardwareMonitorService(
            IHardwareTelemetrySource hardwareTelemetrySource,
            ISystemUsageReader systemUsageReader,
            IOptionalTemperatureReader temperatureReader,
            Action<string, Exception>? logError = null)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
            _systemUsageReader = systemUsageReader ?? throw new ArgumentNullException(nameof(systemUsageReader));
            _temperatureReader = temperatureReader ?? throw new ArgumentNullException(nameof(temperatureReader));
            _logError = logError ?? Models.Logger.Error;
        }

        public HardwareStatsSnapshot ReadSnapshot()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return CreateEmptySnapshot();
                }

                HardwareStatsSnapshot? usageSnapshot = null;
                float? cpuTemp = null;
                float? gpuTemp = null;

                try
                {
                    usageSnapshot = _systemUsageReader.ReadSnapshot();
                }
                catch (Exception ex)
                {
                    LogReadErrorOnce("SystemUsage", "HardwareMonitor: Systemauslastung konnte nicht gelesen werden", ex);
                }

                try
                {
                    cpuTemp = _temperatureReader.TryReadCpuTemperature();
                }
                catch (Exception ex)
                {
                    LogReadErrorOnce("CpuTemperature", "HardwareMonitor: CPU-Temperatur konnte nicht gelesen werden", ex);
                }

                try
                {
                    gpuTemp = _temperatureReader.TryReadGpuTemperature();
                }
                catch (Exception ex)
                {
                    LogReadErrorOnce("GpuTemperature", "HardwareMonitor: GPU-Temperatur konnte nicht gelesen werden", ex);
                }

                return new HardwareStatsSnapshot(
                    CpuTemp: cpuTemp,
                    CpuUsage: usageSnapshot?.CpuUsage,
                    GpuTemp: gpuTemp,
                    GpuUsage: usageSnapshot?.GpuUsage,
                    RamUsedGb: usageSnapshot?.RamUsedGb,
                    RamTotalGb: usageSnapshot?.RamTotalGb,
                    RamLoad: usageSnapshot?.RamLoad,
                    VramUsedGb: usageSnapshot?.VramUsedGb,
                    VramTotalGb: usageSnapshot?.VramTotalGb,
                    VramLoad: usageSnapshot?.VramLoad);
            }
        }

        public Task<HardwareStatsSnapshot> ReadSnapshotAsync() => Task.Run(ReadSnapshot);

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                DisposeResource(_systemUsageReader, "Systemwerte-Reader");

                if (_temperatureReader is IDisposable disposable)
                {
                    DisposeResource(disposable, "Temperatur-Reader");
                }

                DisposeResource(_hardwareTelemetrySource, "Hardwarequelle");
            }
        }

        private static HardwareStatsSnapshot CreateEmptySnapshot() => new(
            CpuTemp: null,
            CpuUsage: null,
            GpuTemp: null,
            GpuUsage: null,
            RamUsedGb: null,
            RamTotalGb: null,
            RamLoad: null,
            VramUsedGb: null,
            VramTotalGb: null,
            VramLoad: null);

        private void DisposeResource(IDisposable disposable, string resourceName)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                LogReadErrorOnce($"Dispose:{resourceName}", $"HardwareMonitor: {resourceName} konnte nicht freigegeben werden", ex);
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
    }
}
