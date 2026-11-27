using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLauncher.Services;

internal readonly record struct GpuReading(float? Temperature, float? Usage, ulong? UsedBytes, ulong? TotalBytes);

internal sealed class GpuTelemetryReader : IDisposable
{
    private readonly IHardwareTelemetrySource _sensors;
    private readonly WindowsGpuCounters _counters = new();
    private readonly SensorUpdateThrottle _adapterRetry = new(TimeSpan.FromSeconds(30));
    private readonly SensorUpdateThrottle _nvmlRetry = new(TimeSpan.FromSeconds(30));
    private List<GpuAdapter> _adapters = new();
    private GpuAdapter? _selected;
    private NvmlGpuReader? _nvml;

    public GpuTelemetryReader(IHardwareTelemetrySource sensors) => _sensors = sensors;

    public GpuReading Read()
    {
        if (_selected == null && _adapterRetry.ShouldUpdate())
        {
            _adapters = WindowsGpuAdapter.Enumerate();
            _selected = SelectAdapter(_adapters);
            if (_selected is GpuAdapter selected)
                Models.Logger.Log($"HardwareMonitor: GPU {selected.Name} (LUID 0x{selected.Luid:X16}) ausgewählt.");
        }
        if (_selected is not GpuAdapter adapter)
            return default;

        if (adapter.VendorId == GpuAdapter.Nvidia && _nvml == null && _nvmlRetry.ShouldUpdate())
            _nvml = NvmlGpuReader.TryOpen(adapter, WindowsGpuAdapter.ReadPciAddress(adapter.Luid));

        var windows = _counters.Read(adapter.Luid);
        GpuReading nvidia = _nvml?.Read() ?? default;
        return Combine(adapter, _adapters, windows, nvidia, _sensors);
    }

    internal static GpuReading Combine(GpuAdapter adapter, IReadOnlyList<GpuAdapter> adapters,
        (float? Usage, ulong? UsedBytes) windows, GpuReading nvidia, IHardwareTelemetrySource source)
    {
        (float? Temperature, float? Usage) sensors = default;
        if ((nvidia.Temperature == null || (windows.Usage == null && nvidia.Usage == null))
            && adapters.Count(candidate => candidate.VendorId == adapter.VendorId) == 1)
            sensors = source.TryReadGpu(adapter.VendorId);

        // Windows und NVML rechnen reservierten Speicher unterschiedlich ab.
        // Bei NVML als Ersatz werden Belegung und Größe gemeinsam übernommen.
        ulong? total = adapter.DedicatedBytes > 0 ? adapter.DedicatedBytes : null;
        ulong? used = windows.UsedBytes;
        if (used == null && nvidia.UsedBytes != null && nvidia.TotalBytes != null)
        {
            used = nvidia.UsedBytes;
            total = nvidia.TotalBytes;
        }
        return new GpuReading(nvidia.Temperature ?? sensors.Temperature,
            windows.Usage ?? nvidia.Usage ?? sensors.Usage, used, total);
    }

    internal static GpuAdapter? SelectAdapter(IReadOnlyList<GpuAdapter> adapters)
    {
        // Die GPU bleibt während der Sitzung stabil. Auf Hybrid-Systemen hat
        // die leistungsfähigere Karte in der Regel mehr dedizierten Speicher.
        GpuAdapter? selected = null;
        foreach (var adapter in adapters)
        {
            if (selected is not GpuAdapter current || adapter.DedicatedBytes > current.DedicatedBytes
                || (adapter.DedicatedBytes == current.DedicatedBytes && adapter.Luid < current.Luid))
                selected = adapter;
        }
        return selected;
    }

    public void Dispose()
    {
        _counters.Dispose();
        _nvml?.Dispose();
    }
}
