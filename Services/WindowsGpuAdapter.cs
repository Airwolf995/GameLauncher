using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GameLauncher.Services;

internal readonly record struct GpuAdapter(ulong Luid, uint VendorId, uint DeviceId, ulong DedicatedBytes, string Name)
{
    public const uint Nvidia = 0x10de;
    public const uint Amd = 0x1002;
    public const uint Intel = 0x8086;
}

internal readonly record struct GpuPciAddress(uint Bus, uint Device, uint Function);

internal static class WindowsGpuAdapter
{
    private const int AdapterAddressQuery = 6; // KMTQAITYPE_ADAPTERADDRESS
    private const int PhysicalAdapterCountQuery = 30; // KMTQAITYPE_PHYSICALADAPTERCOUNT
    private const uint SoftwareOrRemote = 3; // DXGI_ADAPTER_FLAG_REMOTE | DXGI_ADAPTER_FLAG_SOFTWARE
    private const int DxgiAdapterNotFound = unchecked((int)0x887a0002);

    public static List<GpuAdapter> Enumerate()
    {
        var adapters = new List<GpuAdapter>();
        var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(in factoryId, out nint factory) < 0)
            return adapters;

        try
        {
            var enumAdapters = GetMethod<EnumAdapters1>(factory, 12);
            for (uint index = 0; ; index++)
            {
                int result = enumAdapters(factory, index, out nint adapter);
                if (result == DxgiAdapterNotFound)
                    return adapters;
                if (result < 0)
                    return new List<GpuAdapter>();

                try
                {
                    var getDescription = GetMethod<GetDesc1>(adapter, 10);
                    if (getDescription(adapter, out var desc) < 0)
                        return new List<GpuAdapter>();
                    if ((desc.Flags & SoftwareOrRemote) == 0)
                    {
                        adapters.Add(new GpuAdapter(
                            ((ulong)desc.Luid.High << 32) | desc.Luid.Low, desc.VendorId, desc.DeviceId,
                            desc.DedicatedVideoMemory, desc.Description));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    public static GpuPciAddress? ReadPciAddress(ulong luid)
    {
        var open = new OpenAdapter { Luid = new AdapterLuid((uint)luid, (uint)(luid >> 32)) };
        if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
            return null;

        nint buffer = Marshal.AllocHGlobal(sizeof(uint) * 3);
        try
        {
            var query = new QueryAdapter { Handle = open.Handle, Type = PhysicalAdapterCountQuery, Data = buffer, Size = sizeof(uint) };
            // NVML meldet einzelne physische Geräte. Ein verknüpfter DXGI-Adapter
            // lässt sich deshalb nicht mit einem einzelnen NVML-Gerät kombinieren.
            if (D3DKMTQueryAdapterInfo(ref query) != 0 || Marshal.ReadInt32(buffer) != 1)
                return null;

            query.Type = AdapterAddressQuery;
            query.Size = sizeof(uint) * 3;
            if (D3DKMTQueryAdapterInfo(ref query) != 0)
                return null;

            return new GpuPciAddress(
                (uint)Marshal.ReadInt32(buffer), (uint)Marshal.ReadInt32(buffer, 4), (uint)Marshal.ReadInt32(buffer, 8));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            D3DKMTCloseAdapter(in open.Handle);
        }
    }

    private static T GetMethod<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1(nint factory, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1(nint adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct AdapterLuid(uint Low, uint High);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public AdapterLuid Luid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter
    {
        public AdapterLuid Luid;
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapter
    {
        public uint Handle;
        public int Type;
        public nint Data;
        public uint Size;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDXGIFactory1(in Guid factoryId, out nint factory);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter open);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapter query);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(in uint handle);
}
