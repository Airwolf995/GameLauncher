using System;
using System.Threading.Tasks;

namespace GameLauncher.Services
{
    public class HardwareMonitorService : IDisposable
    {
        private readonly IHardwareTelemetrySource _hardwareTelemetrySource;
        private readonly object _sync = new();
        private readonly SystemUsageReader _systemUsageReader;
        private readonly IOptionalTemperatureReader _temperatureReader;

        public HardwareMonitorService()
            : this(new LibreHardwareTelemetrySource())
        {
        }

        internal HardwareMonitorService(IHardwareTelemetrySource hardwareTelemetrySource)
            : this(
                hardwareTelemetrySource,
                new SystemUsageReader(hardwareTelemetrySource),
                new SensorTemperatureReader(hardwareTelemetrySource))
        {
        }

        internal HardwareMonitorService(
            IHardwareTelemetrySource hardwareTelemetrySource,
            SystemUsageReader systemUsageReader,
            IOptionalTemperatureReader temperatureReader)
        {
            _hardwareTelemetrySource = hardwareTelemetrySource ?? throw new ArgumentNullException(nameof(hardwareTelemetrySource));
            _systemUsageReader = systemUsageReader ?? throw new ArgumentNullException(nameof(systemUsageReader));
            _temperatureReader = temperatureReader ?? throw new ArgumentNullException(nameof(temperatureReader));
        }

        public HardwareStatsSnapshot ReadSnapshot()
        {
            lock (_sync)
            {
                HardwareStatsSnapshot usageSnapshot = _systemUsageReader.ReadSnapshot();
                float? cpuTemp = _temperatureReader.TryReadCpuTemperature();
                float? gpuTemp = _temperatureReader.TryReadGpuTemperature();

                return new HardwareStatsSnapshot(
                    CpuTemp: cpuTemp,
                    CpuUsage: usageSnapshot.CpuUsage,
                    GpuTemp: gpuTemp,
                    GpuUsage: usageSnapshot.GpuUsage,
                    RamUsedGb: usageSnapshot.RamUsedGb,
                    RamTotalGb: usageSnapshot.RamTotalGb,
                    RamLoad: usageSnapshot.RamLoad,
                    VramUsedGb: usageSnapshot.VramUsedGb,
                    VramTotalGb: usageSnapshot.VramTotalGb,
                    VramLoad: usageSnapshot.VramLoad);
            }
        }

        public async Task<HardwareStatsSnapshot> ReadSnapshotAsync()
        {
            return await Task.Run(ReadSnapshot);
        }

        public void Dispose()
        {
            _systemUsageReader.Dispose();
            _hardwareTelemetrySource.Dispose();

            if (_temperatureReader is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
