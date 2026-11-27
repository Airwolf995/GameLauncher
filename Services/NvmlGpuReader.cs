using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace GameLauncher.Services;

internal sealed class NvmlGpuReader : IDisposable
{
    private const uint SearchDllLoadDirectory = 0x100;
    private const uint SearchSystem32 = 0x800;
    private readonly nint _library;
    private readonly nint _device;
    private readonly Lifecycle _shutdown;
    private readonly ReadTemperature? _readTemperature;
    private readonly ReadUtilization? _readUtilization;
    private readonly ReadMemory? _readMemory;
    private bool _disposed;

    private NvmlGpuReader(nint library, nint device, Lifecycle shutdown)
    {
        _library = library;
        _device = device;
        _shutdown = shutdown;
        _readTemperature = GetExport<ReadTemperature>(library, "nvmlDeviceGetTemperature");
        _readUtilization = GetExport<ReadUtilization>(library, "nvmlDeviceGetUtilizationRates");
        _readMemory = GetExport<ReadMemory>(library, "nvmlDeviceGetMemoryInfo");
    }

    public static NvmlGpuReader? TryOpen(GpuAdapter adapter, GpuPciAddress? address, Func<nint>? loadLibrary = null)
    {
        if (adapter.VendorId != GpuAdapter.Nvidia || address is not GpuPciAddress pciAddress)
            return null;

        nint library = loadLibrary == null ? LoadDriverLibrary() : loadLibrary();
        if (library == 0)
            return null;

        Lifecycle? shutdown = GetExport<Lifecycle>(library, "nvmlShutdown");
        bool initialized = false;
        NvmlGpuReader? reader = null;
        try
        {
            var initialize = GetExport<Lifecycle>(library, "nvmlInit_v2");
            if (shutdown == null || initialize == null || initialize() != 0)
                return null;
            initialized = true;

            nint device = FindDevice(library, adapter, pciAddress);
            if (device != 0)
                reader = new NvmlGpuReader(library, device, shutdown);
            return reader;
        }
        finally
        {
            if (reader == null)
            {
                if (initialized)
                    shutdown!();
                FreeLibrary(library);
            }
        }
    }

    private static nint LoadDriverLibrary()
    {
        string systemPath = Path.Combine(Environment.SystemDirectory, "nvml.dll");
        nint library = LoadLibraryEx(systemPath, 0, SearchSystem32);
        if (library != 0)
            return library;

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(programFiles))
            return 0;
        string path = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll");
        return LoadLibraryEx(path, 0, SearchDllLoadDirectory | SearchSystem32);
    }

    private static nint FindDevice(nint library, GpuAdapter adapter, GpuPciAddress address)
    {
        var getCount = GetExport<GetCount>(library, "nvmlDeviceGetCount_v2");
        var getDevice = GetExport<GetDevice>(library, "nvmlDeviceGetHandleByIndex_v2");
        var getPciInfo = GetExport<GetPciInfo>(library, "nvmlDeviceGetPciInfo_v3");
        if (getCount == null || getDevice == null || getPciInfo == null || getCount(out uint count) != 0)
            return 0;

        var devices = new List<(nint Handle, GpuPciAddress Address, uint PciDeviceId)>();
        for (uint index = 0; index < count; index++)
        {
            if (getDevice(index, out nint device) != 0 || getPciInfo(device, out var pci) != 0)
                return 0;
            int separator = pci.BusId.LastIndexOf('.');
            if (separator < 0 || !uint.TryParse(pci.BusId.AsSpan(separator + 1), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint function))
                return 0;

            devices.Add((device, new GpuPciAddress(pci.Bus, pci.Device, function), pci.PciDeviceId));
        }
        return MatchPciDevice(adapter, address, devices);
    }

    internal static nint MatchPciDevice(GpuAdapter adapter, GpuPciAddress address,
        IReadOnlyList<(nint Handle, GpuPciAddress Address, uint PciDeviceId)> devices)
    {
        nint match = 0;
        foreach (var device in devices)
        {
            if (address != device.Address || device.PciDeviceId != ((adapter.DeviceId << 16) | adapter.VendorId))
                continue;

            // Die Windows-Abfrage enthält kein separates PCI-Segment. Bei
            // mehrfach passender Adresse bleibt die Zuordnung daher offen.
            if (match != 0)
                return 0;
            match = device.Handle;
        }
        return match;
    }

    public GpuReading Read()
    {
        // Die ältere Temperaturfunktion ist auch in Treibern ohne
        // nvmlDeviceGetTemperatureV verfügbar. Sensor 0 ist NVML_TEMPERATURE_GPU.
        float? temperature = _readTemperature != null && _readTemperature(_device, 0, out uint temp) == 0
            && TemperatureSensorSelection.IsPlausibleTemperature(temp) ? temp : null;
        float? usage = _readUtilization != null && _readUtilization(_device, out var utilization) == 0
            && utilization.Gpu <= 100 ? utilization.Gpu : null;
        if (_readMemory != null && _readMemory(_device, out var memory) == 0 && memory.Total > 0 && memory.Used <= memory.Total)
            return new GpuReading(temperature, usage, memory.Used, memory.Total);

        return new GpuReading(temperature, usage, null, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _shutdown();
        FreeLibrary(_library);
    }

    private static T? GetExport<T>(nint library, string name) where T : Delegate =>
        NativeLibrary.TryGetExport(library, name, out nint function)
            ? Marshal.GetDelegateForFunctionPointer<T>(function)
            : null;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Lifecycle();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCount(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDevice(uint index, out nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetPciInfo(nint device, out PciInfo pci);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadTemperature(nint device, int sensor, out uint temperature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadUtilization(nint device, out Utilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadMemory(nint device, out Memory memory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct PciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string LegacyBusId;
        public uint Domain, Bus, Device, PciDeviceId, SubSystemId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string BusId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu, Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public ulong Total, Free, Used;
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryEx(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint library);
}
