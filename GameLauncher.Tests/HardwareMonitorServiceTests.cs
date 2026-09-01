using System;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public sealed class HardwareMonitorServiceTests
    {
        [Fact]
        public void ReadSnapshot_WhenSystemUsageReaderFails_ReturnsTemperatures()
        {
            using var service = CreateService(
                new TestSystemUsageReader(() => throw new InvalidOperationException("Systemwerte nicht verfügbar")),
                new TestTemperatureReader(cpuTemperature: 61f, gpuTemperature: 72f));

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Equal(61f, snapshot.CpuTemp);
            Assert.Equal(72f, snapshot.GpuTemp);
            Assert.Null(snapshot.CpuUsage);
            Assert.Null(snapshot.RamUsedGb);
            Assert.Null(snapshot.GpuUsage);
            Assert.Null(snapshot.VramUsedGb);
        }

        [Fact]
        public void ReadSnapshot_WhenCpuTemperatureFails_ReturnsSystemValuesAndGpuTemperature()
        {
            using var service = CreateService(
                new TestSystemUsageReader(CreateUsageSnapshot),
                new TestTemperatureReader(
                    cpuException: new InvalidOperationException("CPU-Sensor nicht verfügbar"),
                    gpuTemperature: 72f));

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Null(snapshot.CpuTemp);
            Assert.Equal(72f, snapshot.GpuTemp);
            AssertUsageValues(snapshot);
        }

        [Fact]
        public void ReadSnapshot_WhenGpuTemperatureFails_ReturnsSystemValuesAndCpuTemperature()
        {
            using var service = CreateService(
                new TestSystemUsageReader(CreateUsageSnapshot),
                new TestTemperatureReader(
                    cpuTemperature: 61f,
                    gpuException: new InvalidOperationException("GPU-Sensor nicht verfügbar")));

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Equal(61f, snapshot.CpuTemp);
            Assert.Null(snapshot.GpuTemp);
            AssertUsageValues(snapshot);
        }

        [Fact]
        public void SystemUsageReader_WhenGpuUsageFails_PreservesCpuAndRamValues()
        {
            using var reader = new SystemUsageReader(
                new NullHardwareTelemetrySource(),
                readCpuUsage: () => 42f,
                readMemoryStats: () => (6f, 16f, 38f),
                readGpuUsage: () => throw new InvalidOperationException("GPU-Counter nicht verfügbar"),
                readVramStats: () => (2f, 8f, 25f));

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(42f, snapshot.CpuUsage);
            Assert.Equal(6f, snapshot.RamUsedGb);
            Assert.Equal(16f, snapshot.RamTotalGb);
            Assert.Equal(38f, snapshot.RamLoad);
            Assert.Null(snapshot.GpuUsage);
            Assert.Equal(2f, snapshot.VramUsedGb);
        }

        [Fact]
        public void SystemUsageReader_WhenVramFails_PreservesCpuRamAndGpuValues()
        {
            using var reader = new SystemUsageReader(
                new NullHardwareTelemetrySource(),
                readCpuUsage: () => 42f,
                readMemoryStats: () => (6f, 16f, 38f),
                readGpuUsage: () => 75f,
                readVramStats: () => throw new InvalidOperationException("VRAM-Counter nicht verfügbar"));

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(42f, snapshot.CpuUsage);
            Assert.Equal(6f, snapshot.RamUsedGb);
            Assert.Equal(75f, snapshot.GpuUsage);
            Assert.Null(snapshot.VramUsedGb);
            Assert.Null(snapshot.VramTotalGb);
            Assert.Null(snapshot.VramLoad);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var usageReader = new TestSystemUsageReader(CreateUsageSnapshot);
            var telemetrySource = new TestHardwareTelemetrySource();
            var service = new HardwareMonitorService(
                telemetrySource,
                usageReader,
                new TestTemperatureReader());

            service.Dispose();
            service.Dispose();

            Assert.Equal(1, usageReader.DisposeCalls);
            Assert.Equal(1, telemetrySource.DisposeCalls);
        }

        [Fact]
        public void ReadSnapshot_AfterDispose_ReturnsEmptySnapshotWithoutReadingHardware()
        {
            var usageReader = new TestSystemUsageReader(CreateUsageSnapshot);
            using var service = CreateService(usageReader, new TestTemperatureReader(cpuTemperature: 61f, gpuTemperature: 72f));
            service.Dispose();

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Equal(0, usageReader.ReadCalls);
            Assert.Null(snapshot.CpuTemp);
            Assert.Null(snapshot.CpuUsage);
            Assert.Null(snapshot.GpuTemp);
            Assert.Null(snapshot.GpuUsage);
            Assert.Null(snapshot.RamUsedGb);
            Assert.Null(snapshot.RamTotalGb);
            Assert.Null(snapshot.RamLoad);
            Assert.Null(snapshot.VramUsedGb);
            Assert.Null(snapshot.VramTotalGb);
            Assert.Null(snapshot.VramLoad);
        }

        [Fact]
        public async Task Dispose_DuringRead_WaitsForReadBeforeReleasingResources()
        {
            using var readStarted = new ManualResetEventSlim();
            using var disposeStarted = new ManualResetEventSlim();
            using var allowReadToFinish = new ManualResetEventSlim();
            var usageReader = new TestSystemUsageReader(() =>
            {
                readStarted.Set();
                allowReadToFinish.Wait();
                return CreateUsageSnapshot();
            });
            var telemetrySource = new TestHardwareTelemetrySource();
            using var service = new HardwareMonitorService(
                telemetrySource,
                usageReader,
                new TestTemperatureReader());

            Task<HardwareStatsSnapshot> readTask = Task.Run(service.ReadSnapshot);
            Assert.True(readStarted.Wait(TimeSpan.FromSeconds(2)));

            Task disposeTask = Task.Run(() =>
            {
                disposeStarted.Set();
                service.Dispose();
            });
            Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(2)));
            await Task.Delay(100);
            Assert.Equal(0, usageReader.DisposeCalls);
            Assert.Equal(0, telemetrySource.DisposeCalls);

            allowReadToFinish.Set();
            await readTask;
            await disposeTask;

            Assert.Equal(1, usageReader.DisposeCalls);
            Assert.Equal(1, telemetrySource.DisposeCalls);
        }

        [Fact]
        public void ReadSnapshot_WhenSameAreaFailsRepeatedly_LogsOnlyOnce()
        {
            int errorCount = 0;
            using var service = CreateService(
                new TestSystemUsageReader(() => throw new InvalidOperationException("Systemwerte nicht verfügbar")),
                new TestTemperatureReader(),
                (_, _) => errorCount++);

            service.ReadSnapshot();
            service.ReadSnapshot();

            Assert.Equal(1, errorCount);
        }

        [Fact]
        public void SystemUsageReader_WhenSameAreaFailsRepeatedly_LogsOnlyOnce()
        {
            int errorCount = 0;
            using var reader = new SystemUsageReader(
                new NullHardwareTelemetrySource(),
                readCpuUsage: () => 42f,
                readMemoryStats: () => (6f, 16f, 38f),
                readGpuUsage: () => throw new InvalidOperationException("GPU-Counter nicht verfügbar"),
                readVramStats: () => (2f, 8f, 25f),
                logError: (_, _) => errorCount++);

            reader.ReadSnapshot();
            reader.ReadSnapshot();

            Assert.Equal(1, errorCount);
        }

        private static HardwareMonitorService CreateService(
            ISystemUsageReader usageReader,
            IOptionalTemperatureReader temperatureReader,
            Action<string, Exception>? logError = null)
        {
            return new HardwareMonitorService(
                new TestHardwareTelemetrySource(),
                usageReader,
                temperatureReader,
                logError);
        }

        private static HardwareStatsSnapshot CreateUsageSnapshot() => new(
            CpuTemp: null,
            CpuUsage: 42f,
            GpuTemp: null,
            GpuUsage: 75f,
            RamUsedGb: 6f,
            RamTotalGb: 16f,
            RamLoad: 38f,
            VramUsedGb: 2f,
            VramTotalGb: 8f,
            VramLoad: 25f);

        private static void AssertUsageValues(HardwareStatsSnapshot snapshot)
        {
            Assert.Equal(42f, snapshot.CpuUsage);
            Assert.Equal(75f, snapshot.GpuUsage);
            Assert.Equal(6f, snapshot.RamUsedGb);
            Assert.Equal(16f, snapshot.RamTotalGb);
            Assert.Equal(38f, snapshot.RamLoad);
            Assert.Equal(2f, snapshot.VramUsedGb);
            Assert.Equal(8f, snapshot.VramTotalGb);
            Assert.Equal(25f, snapshot.VramLoad);
        }

        /// <summary>
        /// Die geraetebezogene Angabe des Treibers hat Vorrang vor den
        /// Leistungsindikatoren. Diese sind prozessbezogen und erfassen nur
        /// Prozesse, die beim Aufbau der Zaehlerliste schon liefen - ein spaeter
        /// gestartetes Spiel blieb dort unsichtbar, die Anzeige stand bei 1 %.
        /// </summary>
        [Fact]
        public void SystemUsageReader_PrefersDeviceGpuLoadOverPerformanceCounters()
        {
            using var reader = new SystemUsageReader(
                new TestHardwareTelemetrySource(gpuLoad: 87f),
                readCpuUsage: () => 12f,
                readMemoryStats: () => (8f, 32f, 25f),
                readGpuUsage: null,
                readVramStats: () => (2f, 8f, 25f));

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(87f, snapshot.GpuUsage);
        }

        /// <summary>
        /// Leerlauf sind null Prozent - das ist ein gueltiger Messwert und darf
        /// nicht als "kein Wert" behandelt werden.
        /// </summary>
        [Fact]
        public void SystemUsageReader_AcceptsZeroPercentAsAValidGpuLoad()
        {
            using var reader = new SystemUsageReader(
                new TestHardwareTelemetrySource(gpuLoad: 0f),
                readCpuUsage: () => 12f,
                readMemoryStats: () => (8f, 32f, 25f),
                readGpuUsage: null,
                readVramStats: () => (2f, 8f, 25f));

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(0f, snapshot.GpuUsage);
        }

        [Fact]
        public void SystemUsageReader_ClampsImplausibleDeviceGpuLoad()
        {
            using var reader = new SystemUsageReader(
                new TestHardwareTelemetrySource(gpuLoad: 140f),
                readCpuUsage: () => 12f,
                readMemoryStats: () => (8f, 32f, 25f),
                readGpuUsage: null,
                readVramStats: () => (2f, 8f, 25f));

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(100f, snapshot.GpuUsage);
        }

        private sealed class TestSystemUsageReader : ISystemUsageReader
        {
            private readonly Func<HardwareStatsSnapshot> _readSnapshot;

            public TestSystemUsageReader(Func<HardwareStatsSnapshot> readSnapshot)
            {
                _readSnapshot = readSnapshot;
            }

            public int DisposeCalls { get; private set; }
            public int ReadCalls { get; private set; }

            public HardwareStatsSnapshot ReadSnapshot()
            {
                ReadCalls++;
                return _readSnapshot();
            }

            public void Dispose()
            {
                DisposeCalls++;
            }
        }

        private sealed class TestTemperatureReader : IOptionalTemperatureReader
        {
            private readonly Exception? _cpuException;
            private readonly float? _cpuTemperature;
            private readonly Exception? _gpuException;
            private readonly float? _gpuTemperature;

            public TestTemperatureReader(
                float? cpuTemperature = null,
                float? gpuTemperature = null,
                Exception? cpuException = null,
                Exception? gpuException = null)
            {
                _cpuTemperature = cpuTemperature;
                _gpuTemperature = gpuTemperature;
                _cpuException = cpuException;
                _gpuException = gpuException;
            }

            public float? TryReadCpuTemperature()
            {
                if (_cpuException != null)
                {
                    throw _cpuException;
                }

                return _cpuTemperature;
            }

            public float? TryReadGpuTemperature()
            {
                if (_gpuException != null)
                {
                    throw _gpuException;
                }

                return _gpuTemperature;
            }
        }

        private sealed class TestHardwareTelemetrySource : IHardwareTelemetrySource
        {
            private readonly float? _gpuLoad;

            public TestHardwareTelemetrySource(float? gpuLoad = null) => _gpuLoad = gpuLoad;

            public int DisposeCalls { get; private set; }

            public float? TryReadCpuTemperature() => null;
            public float? TryReadGpuTemperature() => null;
            public float? TryReadGpuLoad() => _gpuLoad;
            public float? TryReadGpuMemoryTotalGb() => null;

            public void Dispose()
            {
                DisposeCalls++;
            }
        }
    }
}
