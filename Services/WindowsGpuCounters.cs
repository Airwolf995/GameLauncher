using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace GameLauncher.Services;

internal sealed class WindowsGpuCounters : IDisposable
{
    private const uint MoreData = 0x800007d2;
    private const uint FormatDouble = 0x00000200;
    private const uint NoCapAt100 = 0x00008000;
    private static readonly int ItemSize = Marshal.SizeOf<CounterItem>();
    private nint _query, _usage, _memory, _buffer;
    private uint _capacity;
    private readonly SensorUpdateThrottle _retry = new(TimeSpan.FromSeconds(30));

    public (float? Usage, ulong? UsedBytes) Read(ulong luid)
    {
        if (_query == 0 && _retry.ShouldUpdate())
            Open();
        if (_query == 0 || PdhCollectQueryData(_query) != 0)
            return default;

        var usage = ReadArray(_usage);
        var memory = ReadArray(_memory);
        return (AggregateUsage(luid, usage), AggregateMemory(luid, memory));
    }

    private void Open()
    {
        if (PdhOpenQuery(null, 0, out _query) != 0)
        {
            _query = 0;
            return;
        }

        if (PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _usage) != 0)
            _usage = 0;
        if (PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out _memory) != 0)
            _memory = 0;
        if (_usage == 0 && _memory == 0)
            Dispose();
    }

    private List<(string Name, double Value)> ReadArray(nint counter)
    {
        var values = new List<(string, double)>();
        if (counter == 0)
            return values;

        uint size = _capacity;
        uint status = PdhGetFormattedCounterArray(counter, FormatDouble | NoCapAt100, ref size, out uint count, _buffer);
        if (status == MoreData && size > _capacity)
        {
            _buffer = _buffer == 0
                ? Marshal.AllocHGlobal((nint)size)
                : Marshal.ReAllocHGlobal(_buffer, (nint)size);
            _capacity = size;
            status = PdhGetFormattedCounterArray(counter, FormatDouble | NoCapAt100, ref size, out count, _buffer);
        }

        if (status != 0)
            return values;

        for (int index = 0; index < count; index++)
        {
            var item = Marshal.PtrToStructure<CounterItem>(_buffer + index * ItemSize);
            if (item.Value.Status <= 1 && double.IsFinite(item.Value.Number) && item.Value.Number >= 0)
            {
                string? name = Marshal.PtrToStringUni(item.Name);
                if (name != null)
                    values.Add((name, item.Value.Number));
            }
        }

        return values;
    }

    internal static float? AggregateUsage(ulong luid, IEnumerable<(string Name, double Value)> readings)
    {
        var engines = new Dictionary<(uint Physical, uint Engine), double>();
        foreach (var (name, value) in readings)
        {
            if (!double.IsFinite(value) || value < 0 || !name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)
                || !TryReadInstance(name, luid, out uint physical, out var suffix)
                || !suffix.StartsWith("_eng_", StringComparison.OrdinalIgnoreCase))
                continue;

            suffix = suffix[5..];
            int end = suffix.IndexOf('_');
            if (end < 0 || !uint.TryParse(suffix[..end], NumberStyles.None, CultureInfo.InvariantCulture, out uint engine))
                continue;

            var key = (physical, engine);
            engines[key] = engines.GetValueOrDefault(key) + value;
        }

        // Prozesse auf derselben Engine teilen sich deren Zeit. Verschiedene
        // Engines können parallel laufen, daher zählt die am stärksten genutzte.
        double? busiest = null;
        foreach (double engineUsage in engines.Values)
            busiest = Math.Max(busiest ?? 0, engineUsage);
        return busiest is double percent ? (float)Math.Min(percent, 100) : null;
    }

    internal static ulong? AggregateMemory(ulong luid, IEnumerable<(string Name, double Value)> readings)
    {
        ulong? bytes = null;
        foreach (var (name, value) in readings)
        {
            if (double.IsFinite(value) && value >= 0 && value < ulong.MaxValue
                && name.StartsWith("luid_", StringComparison.OrdinalIgnoreCase)
                && TryReadInstance(name, luid, out _, out var suffix) && suffix.IsEmpty)
                bytes = (bytes ?? 0) + (ulong)value;
        }
        return bytes;
    }

    private static bool TryReadInstance(ReadOnlySpan<char> name, ulong luid, out uint physical, out ReadOnlySpan<char> suffix)
    {
        physical = 0;
        suffix = default;
        int start = name.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        ReadOnlySpan<char> fields = name[(start + 7)..];
        if (fields.Length < 25
            || !fields[8..].StartsWith("_0x", StringComparison.OrdinalIgnoreCase)
            || !fields[19..].StartsWith("_phys_", StringComparison.OrdinalIgnoreCase)
            || !uint.TryParse(fields[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint high)
            || !uint.TryParse(fields.Slice(11, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint low)
            || (((ulong)high << 32) | low) != luid)
            return false;

        fields = fields[25..];
        int end = fields.IndexOf('_');
        if (end >= 0)
            suffix = fields[end..];
        return uint.TryParse(end < 0 ? fields : fields[..end], NumberStyles.None, CultureInfo.InvariantCulture, out physical);
    }

    public void Dispose()
    {
        if (_query != 0)
            PdhCloseQuery(_query);
        _query = _usage = _memory = 0;
        Marshal.FreeHGlobal(_buffer);
        _buffer = 0;
        _capacity = 0;
    }

    // PDH_FMT_COUNTERVALUE enthält nach CStatus eine auf acht Byte
    // ausgerichtete Union. Mit PDH_FMT_DOUBLE lesen wir deren double-Feld.
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public nint Name;
        public CounterValue Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)]
        public uint Status;
        [FieldOffset(8)]
        public double Number;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQuery(string? dataSource, nuint userData, out nint query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounter(nint query, string path, nuint userData, out nint counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, out uint count, nint buffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
}
