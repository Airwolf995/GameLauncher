using System;
using GameLauncher.Services;

namespace GameLauncher.Tests;

public class GpuTelemetryTests
{
    private const ulong GiB = 1UL << 30;
    private const ulong NvidiaLuid = 0x000000010001ff09;
    private const ulong OtherLuid = 0x000000020001ff09;

    [Theory]
    [InlineData(GpuAdapter.Amd)]
    [InlineData(GpuAdapter.Intel)]
    public void HybridSystem_UsesTheDedicatedGpuAcrossSources(uint integratedVendor)
    {
        var integrated = new GpuAdapter(OtherLuid, integratedVendor, 1, 512UL << 20, "iGPU");
        var nvidia = new GpuAdapter(NvidiaLuid, GpuAdapter.Nvidia, 2, 16 * GiB, "dGPU");
        var adapters = new[] { integrated, nvidia };
        var selected = Assert.IsType<GpuAdapter>(GpuTelemetryReader.SelectAdapter(adapters));
        Assert.Equal(selected, GpuTelemetryReader.SelectAdapter(new[] { nvidia, integrated }));

        using var sensors = new SensorSource(integratedVendor == GpuAdapter.Amd ? "gpu-amd/0" : "gpu-intel-integrated/PCI%5CVEN_8086");
        var memory = WindowsGpuCounters.AggregateMemory(selected.Luid, new[]
        {
            ("luid_0x00000002_0x0001ff09_phys_0", (double)GiB / 4),
            ("luid_0x00000001_0x0001ff09_phys_0", 3d * GiB)
        });
        var reading = GpuTelemetryReader.Combine(selected, adapters, (0f, memory), default, sensors);

        Assert.Equal(GpuAdapter.Nvidia, selected.VendorId);
        Assert.Equal(new GpuReading(51f, 0f, 3 * GiB, 16 * GiB), reading);
    }

    [Theory]
    [InlineData(GpuAdapter.Amd, "gpu-amd/0")]
    [InlineData(GpuAdapter.Intel, "gpu-intel/0x56A0")]
    [InlineData(GpuAdapter.Intel, "gpu-intel-integrated/PCI%5CVEN_8086")]
    public void SingleAmdOrIntelGpu_UsesWindowsAndMatchingWebSensors(uint vendor, string sensorType)
    {
        var adapter = new GpuAdapter(OtherLuid, vendor, 1, 8 * GiB, "GPU");
        var adapters = new[] { adapter };
        using var sensors = new SensorSource(sensorType);

        Assert.Equal(adapter, GpuTelemetryReader.SelectAdapter(adapters));
        Assert.Equal(new GpuReading(75f, 88f, 2 * GiB, 8 * GiB),
            GpuTelemetryReader.Combine(adapter, adapters, (88f, 2 * GiB), default, sensors));
    }

    [Fact]
    public void SingleNvidiaGpu_UsesWindowsMemoryOrTheWholeNvmlPair()
    {
        var adapter = new GpuAdapter(NvidiaLuid, GpuAdapter.Nvidia, 1, 15 * GiB, "GPU");
        using var sensors = new SensorSource("gpu-amd/0");
        var nvml = new GpuReading(45f, 0f, 2 * GiB, 16 * GiB);

        Assert.Equal(adapter, GpuTelemetryReader.SelectAdapter(new[] { adapter }));
        Assert.Equal(new GpuReading(45f, 11f, 3 * GiB, 15 * GiB),
            GpuTelemetryReader.Combine(adapter, new[] { adapter }, (11f, 3 * GiB), nvml, sensors));
        Assert.Equal(nvml, GpuTelemetryReader.Combine(adapter, new[] { adapter }, default, nvml, sensors));
    }

    [Fact]
    public void MultipleDedicatedGpuWithSameName_UseLuidAndSkipAmbiguousWebSensors()
    {
        var selected = new GpuAdapter(NvidiaLuid, GpuAdapter.Nvidia, 1, 16 * GiB, "GPU");
        var other = new GpuAdapter(OtherLuid, GpuAdapter.Nvidia, 1, 16 * GiB, "GPU");
        var adapters = new[] { other, selected };
        using var sensors = new SensorSource("gpu-amd/0");

        Assert.Equal(selected, GpuTelemetryReader.SelectAdapter(adapters));
        Assert.Equal(new GpuReading(null, null, null, 16 * GiB),
            GpuTelemetryReader.Combine(selected, adapters, default, default, sensors));
        Assert.Equal(0, sensors.GpuReads);
    }

    [Fact]
    public void MissingNativeLibraryAndSensors_LeaveUnavailableValuesEmpty()
    {
        var adapter = new GpuAdapter(NvidiaLuid, GpuAdapter.Nvidia, 1, 16 * GiB, "GPU");
        using var sensors = new SensorSource("gpu-amd/0", includeNvidia: false);
        Assert.Null(NvmlGpuReader.TryOpen(adapter, new GpuPciAddress(1, 0, 0), loadLibrary: () => 0));
        Assert.Equal(new GpuReading(null, 0f, null, 16 * GiB),
            GpuTelemetryReader.Combine(adapter, new[] { adapter }, (0f, null), default, sensors));
    }

    [Fact]
    public void NvmlMatchesPciAddressRegardlessOfIndexAndRejectsAmbiguity()
    {
        var adapter = new GpuAdapter(NvidiaLuid, GpuAdapter.Nvidia, 2, 16 * GiB, "GPU");
        var address = new GpuPciAddress(1, 0, 0);
        const uint deviceId = (2U << 16) | GpuAdapter.Nvidia;
        var matching = ((nint)200, address, deviceId);
        var otherCard = ((nint)100, new GpuPciAddress(2, 0, 0), deviceId);
        var otherModel = ((nint)300, address, deviceId + (1U << 16));

        Assert.Equal((nint)200, NvmlGpuReader.MatchPciDevice(adapter, address, new[] { otherCard, otherModel, matching }));
        Assert.Equal((nint)200, NvmlGpuReader.MatchPciDevice(adapter, address, new[] { matching, otherCard }));
        Assert.Equal(nint.Zero, NvmlGpuReader.MatchPciDevice(adapter, address, new[] { matching, ((nint)400, address, deviceId) }));
        Assert.Equal(nint.Zero, NvmlGpuReader.MatchPciDevice(adapter, address, new[] { otherCard, otherModel }));
    }

    [Fact]
    public void Usage_SumsProcessesOnOneEngineAndUsesTheBusiestEngineOfTheAdapter()
    {
        var readings = new[]
        {
            ("pid_20_luid_0x00000002_0x0001ff09_phys_0_eng_0_engtype_3D", 100d),
            ("pid_20_luid_0x00000001_0x0001ff09_phys_0_eng_0_engtype_3D", 30d),
            ("pid_21_luid_0x00000001_0x0001ff09_phys_0_eng_0_engtype_3D", 43d),
            ("pid_21_luid_0x00000001_0x0001ff09_phys_0_eng_3_engtype_3D", 33d),
            ("pid_21_luid_0x00000001_0x0001ff09_phys_1_eng_0_engtype_3D", 28d),
            ("pid_22_luid_0x00000001_0x0001ff09_phys_0_eng_0_engtype_3D", double.NaN)
        };

        Assert.Equal(73f, WindowsGpuCounters.AggregateUsage(NvidiaLuid, readings));
        Assert.Equal(0f, WindowsGpuCounters.AggregateUsage(NvidiaLuid, new[] { (readings[1].Item1, 0d) }));
        Assert.Null(WindowsGpuCounters.AggregateUsage(0, readings));
    }

    private sealed class SensorSource : IHardwareTelemetrySource
    {
        private readonly string _payload;

        public SensorSource(string otherDevice, bool includeNvidia = true)
        {
            string other = """{"SensorId":"/GPU_DEVICE/temperature/0","Text":"GPU Core","Type":"Temperature","Value":"75,0 °C"}"""
                .Replace("GPU_DEVICE", otherDevice, StringComparison.Ordinal);
            string nvidia = """{"SensorId":"/gpu-nvidia/7/temperature/0","Text":"GPU Core","Type":"Temperature","Value":"51.0 °C"}""";
            _payload = "{\"Children\":[" + (includeNvidia ? nvidia + "," : "") + other + "]}";
        }

        public int GpuReads { get; private set; }
        public float? TryReadCpuTemperature() => null;
        public void Dispose() { }

        public (float? Temperature, float? Usage) TryReadGpu(uint vendorId)
        {
            GpuReads++;
            return LibreHardwareMonitorWebSource.SelectGpu(LibreHardwareMonitorWebSource.ParseReadings(_payload), vendorId);
        }
    }
}
