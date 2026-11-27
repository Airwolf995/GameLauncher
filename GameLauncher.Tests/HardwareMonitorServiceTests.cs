using System;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public sealed class HardwareMonitorServiceTests
    {
        [Fact]
        public void ReadSnapshot_WhenSystemUsageReaderFails_ReturnsCpuTemperature()
        {
            using var service = CreateService(
                new TestSystemUsageReader(() => throw new InvalidOperationException("Systemwerte nicht verfügbar")),
                new TestTemperatureReader(cpuTemperature: 61f));

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Equal(61f, snapshot.CpuTemp);
            Assert.Null(snapshot.GpuTemp);
            Assert.Null(snapshot.CpuUsage);
            Assert.Null(snapshot.RamUsedGb);
            Assert.Null(snapshot.GpuUsage);
            Assert.Null(snapshot.VramUsedGb);
        }

        [Fact]
        public void ReadSnapshot_WhenCpuTemperatureFails_PreservesUsageAndGpuTemperature()
        {
            using var service = CreateService(
                new TestSystemUsageReader(CreateUsageSnapshot),
                new TestTemperatureReader(cpuException: new InvalidOperationException("CPU-Sensor nicht verfügbar")));

            HardwareStatsSnapshot snapshot = service.ReadSnapshot();

            Assert.Null(snapshot.CpuTemp);
            Assert.Equal(72f, snapshot.GpuTemp);
            AssertUsageValues(snapshot);
        }

        [Fact]
        public void SystemUsageReader_WhenGpuFails_PreservesCpuAndRamValues()
        {
            using var reader = new SystemUsageReader(
                readCpuUsage: () => 42f,
                readMemoryStats: () => (6f, 16f, 38f),
                readGpu: () => throw new InvalidOperationException("GPU-Counter nicht verfügbar"),
                logError: (_, _) => { });

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(42f, snapshot.CpuUsage);
            Assert.Equal(6f, snapshot.RamUsedGb);
            Assert.Equal(16f, snapshot.RamTotalGb);
            Assert.Equal(38f, snapshot.RamLoad);
            Assert.Null(snapshot.GpuTemp);
            Assert.Null(snapshot.GpuUsage);
            Assert.Null(snapshot.VramUsedGb);
            Assert.Null(snapshot.VramTotalGb);
            Assert.Null(snapshot.VramLoad);
        }

        [Theory]
        [InlineData(0, 8, 0)]
        [InlineData(3, 16, 18.75)]
        public void SystemUsageReader_ReportsVramFromOneReading(int used, int total, float percent)
        {
            using var reader = new SystemUsageReader(
                readCpuUsage: () => null,
                readMemoryStats: () => default,
                readGpu: () => new GpuReading(53f, 0f, (ulong)used << 30, (ulong)total << 30),
                logError: (_, _) => { });

            HardwareStatsSnapshot snapshot = reader.ReadSnapshot();

            Assert.Equal(53f, snapshot.GpuTemp);
            Assert.Equal(0f, snapshot.GpuUsage);
            Assert.Equal(used, snapshot.VramUsedGb);
            Assert.Equal(total, snapshot.VramTotalGb);
            Assert.Equal(percent, snapshot.VramLoad);
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
            using var service = CreateService(usageReader, new TestTemperatureReader(cpuTemperature: 61f));
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

            // Eigene Threads statt Thread-Pool: Andere, parallel laufende
            // Testklassen blockieren Pool-Threads, dann starten Lesen und
            // Freigeben mitunter nicht innerhalb der zwei Sekunden unten.
            Task<HardwareStatsSnapshot> readTask = Task.Factory.StartNew(
                service.ReadSnapshot,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(readStarted.Wait(TimeSpan.FromSeconds(2)));

            Task disposeTask = Task.Factory.StartNew(
                () =>
                {
                    disposeStarted.Set();
                    service.Dispose();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
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
                readCpuUsage: () => null,
                readMemoryStats: () => default,
                readGpu: () => throw new InvalidOperationException("GPU-Counter nicht verfügbar"),
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
            GpuTemp: 72f,
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

        private sealed class TestTemperatureReader(
            float? cpuTemperature = null, Exception? cpuException = null) : IOptionalTemperatureReader
        {
            public float? TryReadCpuTemperature() =>
                cpuException == null ? cpuTemperature : throw cpuException;
        }

        private sealed class TestHardwareTelemetrySource : IHardwareTelemetrySource
        {
            public int DisposeCalls { get; private set; }

            public float? TryReadCpuTemperature() => null;
            public (float? Temperature, float? Usage) TryReadGpu(uint vendorId) => default;

            public void Dispose() => DisposeCalls++;
        }
    }
}
