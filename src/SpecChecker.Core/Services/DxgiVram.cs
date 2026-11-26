using System.Runtime.InteropServices;

namespace SpecChecker.Core.Services;

/// <summary>
/// GPU VRAM via DXGI — the same API Task Manager uses to report GPU memory.
/// Vendor-neutral (NVIDIA / AMD / Intel) and 64-bit, unlike WMI's
/// Win32_VideoController.AdapterRAM which is a 32-bit field that caps at 4GB
/// (the reason every modern card showed 4095/4096MB).
///
/// Implementation note: this calls the COM vtables manually via delegates
/// instead of ComImport interfaces. The ComImport version threw
/// InvalidCastException ("Specified cast is not valid") during interface
/// marshaling on real hardware, so there is no RCW and no cast anywhere here
/// — just raw IUnknown pointers, vtable slots verified against dxgi.h:
///   IDXGIFactory1::EnumAdapters1 = slot 12 (3 IUnknown + 4 IDXGIObject + 5 IDXGIFactory)
///   IDXGIAdapter1::GetDesc1      = slot 10 (3 IUnknown + 4 IDXGIObject + 3 IDXGIAdapter)
///
/// Returns dedicated VRAM bytes plus shared system memory bytes, keyed
/// by "VVVV&DDDD" PCI VEN/DEV id, matching the format produced by
/// SpecService.GetPciId from WMI PNPDeviceID. The shared figure matters
/// for integrated GPUs: DXGI reports a tiny dedicated memory (often
/// 128 MB) and the real usable graphics memory as shared system memory,
/// so the shared number is what keeps the label honest.
/// </summary>
internal static class DxgiVram
{
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Fn(IntPtr self, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Fn(IntPtr self, out DxgiAdapterDesc1 desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    // Matches DXGI_ADAPTER_DESC1 in dxgi.h (verified against Microsoft Learn).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    private static T GetVtableFn<T>(IntPtr comObject, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr fnPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fnPtr);
    }

    public static Dictionary<string, (ulong dedicated, ulong shared)> GetVramByPciId()
    {
        var map = new Dictionary<string, (ulong, ulong)>(StringComparer.OrdinalIgnoreCase);
        IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IID_IDXGIFactory1
            int hr = CreateDXGIFactory1(ref iid, out factory);
            if (hr < 0 || factory == IntPtr.Zero)
            {
                Logger.Write($"DXGI: CreateDXGIFactory1 failed, hr=0x{hr:X8}.");
                return map;
            }

            var enumAdapters1 = GetVtableFn<EnumAdapters1Fn>(factory, 12);

            for (uint i = 0; ; i++)
            {
                hr = enumAdapters1(factory, i, out IntPtr adapter);
                if (hr == DXGI_ERROR_NOT_FOUND)
                    break;
                if (hr < 0 || adapter == IntPtr.Zero)
                {
                    Logger.Write($"DXGI: EnumAdapters1({i}) failed, hr=0x{hr:X8}.");
                    break;
                }
                try
                {
                    var getDesc1 = GetVtableFn<GetDesc1Fn>(adapter, 10);
                    hr = getDesc1(adapter, out var desc);
                    if (hr < 0)
                    {
                        Logger.Write($"DXGI: GetDesc1({i}) failed, hr=0x{hr:X8}.");
                        continue;
                    }
                    if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                    {
                        Logger.Write($"DXGI: adapter {i} is a software adapter, skipped.");
                        continue;
                    }
                    ulong vram = desc.DedicatedVideoMemory.ToUInt64();
                    ulong shared = desc.SharedSystemMemory.ToUInt64();
                    Logger.Write($"DXGI: adapter {i}: '{desc.Description}' " +
                                 $"PCI {desc.VendorId:X4}&{desc.DeviceId:X4} VRAM={vram} bytes, Shared={shared} bytes.");
                    if (vram > 0)
                        map[$"{desc.VendorId:X4}&{desc.DeviceId:X4}"] = (vram, shared);
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Write("DXGI VRAM read failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                try { Marshal.Release(factory); }
                catch { }
            }
        }
        return map;
    }
}
