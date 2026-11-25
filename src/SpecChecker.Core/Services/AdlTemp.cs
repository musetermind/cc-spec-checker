using System.Runtime.InteropServices;
using System.Text;

namespace SpecChecker.Core.Services;

/// <summary>
/// AMD GPU temperature via ADL (AMD Display Library) — the driver API, so no
/// kernel driver is needed and it works unelevated. atiadlxx.dll ships with
/// the AMD driver. Functions are resolved by name through GetProcAddress
/// (ADL exports named functions, unlike NVAPI's magic offsets), the same
/// manual pattern as NvApiTemp (no vendor SDK, no static link).
///
/// ADL2 (context) API used — OverdriveN covers RX 400 series and newer,
/// including RDNA3 (RX 7900 XT). Older pre-OverdriveN cards are not covered.
/// Temperature comes back in millidegrees C (divide by 1000).
///
///   ADL2_Main_Control_Create          (malloc callback, enumConnected, out context)
///   ADL2_Main_Control_Destroy         (context)
///   ADL2_Adapter_NumberOfAdapters_Get (context, out count)
///   ADL2_Adapter_AdapterInfo_Get      (context, out info array, input size)
///   ADL2_OverdriveN_Temperature_Get   (context, adapter index, out millidegrees)
/// </summary>
internal static class AdlTemp
{
    private const int ADL_OK = 0;
    private const int ADL_MAX_PATH = 256;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // CRT malloc/free (msvcrt.dll). ADL is a C-runtime DLL and its sample
    // uses the CRT malloc as the callback — the memory ADL allocates through
    // the callback must come from the CRT heap, because ADL may free it with
    // the CRT free. Using Marshal.AllocHGlobal (Windows heap) for the
    // callback caused an access violation (0xc0000005) on the bench:
    // a heap mismatch. So both the callback and the AdapterInfo free below
    // go through msvcrt, matching the ADL sample exactly.
    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr malloc(int size);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void free(IntPtr ptr);

    // ADL malloc callback: void* (__stdcall *)(int). ADL calls it to allocate
    // the AdapterInfo array; we free that pointer ourselves afterwards.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr MallocCallback(int size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MainControlCreateFn(MallocCallback callback, int enumConnectedAdapters, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MainControlDestroyFn(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NumberOfAdaptersFn(IntPtr context, out int numAdapters);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AdapterInfoGetFn(IntPtr context, out IntPtr adapterInfo, int inputSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OverdriveNTemperatureGetFn(IntPtr context, int adapterIndex, out int temperature);

    // AdapterInfo (adl_structures.h). Only iAdapterIndex, iVendorID and
    // strAdapterName are used, but the full layout is needed so the array
    // stride (Marshal.SizeOf) matches what ADL writes.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] AdapterName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] DriverPath;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] DriverPathExt;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL_MAX_PATH)]
        public byte[] PNPString;
        public int OSDisplayIndex;
    }

    /// <summary>GPU temperature in °C keyed by the adapter's display name
    /// (e.g. "AMD Radeon RX 7900 XT"), for matching against the WMI card
    /// name. Returns an empty map when ADL is unavailable (no AMD GPU/driver)
    /// or the card does not support OverdriveN temperature.</summary>
    public static Dictionary<string, int> GetGpuTemps()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hModule = LoadLibrary("atiadlxx.dll");
        if (hModule == IntPtr.Zero)
        {
            Logger.Write("ADL: atiadlxx.dll not present (no AMD driver).");
            return result;
        }

        // Keep the malloc delegate alive for the whole ADL session.
        // CRT malloc (msvcrt) — see the note on the malloc/free imports.
        MallocCallback mallocCb = size => malloc(size);
        IntPtr context = IntPtr.Zero;
        try
        {
            var create = GetFn<MainControlCreateFn>(hModule, "ADL2_Main_Control_Create");
            var destroy = GetFn<MainControlDestroyFn>(hModule, "ADL2_Main_Control_Destroy");
            var numAdapters = GetFn<NumberOfAdaptersFn>(hModule, "ADL2_Adapter_NumberOfAdapters_Get");
            var adapterInfoGet = GetFn<AdapterInfoGetFn>(hModule, "ADL2_Adapter_AdapterInfo_Get");
            var tempGet = GetFn<OverdriveNTemperatureGetFn>(hModule, "ADL2_OverdriveN_Temperature_Get");
            if (create == null || destroy == null || numAdapters == null
                || adapterInfoGet == null || tempGet == null)
            {
                Logger.Write("ADL: could not resolve core functions.");
                return result;
            }

            if (create(mallocCb, 1, out context) != ADL_OK || context == IntPtr.Zero)
            {
                Logger.Write("ADL: ADL2_Main_Control_Create failed.");
                return result;
            }
            try
            {
                if (numAdapters(context, out var count) != ADL_OK || count <= 0)
                {
                    Logger.Write("ADL: no adapters enumerated.");
                    return result;
                }
                var infoSize = Marshal.SizeOf<AdapterInfo>();
                IntPtr infoPtr = IntPtr.Zero;
                if (adapterInfoGet(context, out infoPtr, infoSize * count) != ADL_OK
                    || infoPtr == IntPtr.Zero)
                {
                    Logger.Write("ADL: AdapterInfo_Get failed.");
                    return result;
                }
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var info = Marshal.PtrToStructure<AdapterInfo>(
                            IntPtr.Add(infoPtr, i * infoSize));
                        var name = AnsiBytesToString(info.AdapterName);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            Logger.Write($"ADL: adapter {info.AdapterIndex} has no name, skipped.");
                            continue;
                        }
                        if (tempGet(context, info.AdapterIndex, out var milliC) == ADL_OK)
                        {
                            var tempC = milliC / 1000;
                            Logger.Write($"ADL: '{name}' (index {info.AdapterIndex}): temp={tempC}C.");
                            result[name] = tempC;
                        }
                        else
                        {
                            Logger.Write($"ADL: '{name}' (index {info.AdapterIndex}): OverdriveN temperature not supported.");
                        }
                    }
                }
                finally
                {
                    // The AdapterInfo array was allocated through our CRT
                    // malloc callback, so free it with the CRT free.
                    try { free(infoPtr); } catch { }
                }
            }
            finally
            {
                try { destroy(context); } catch { }
                context = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Logger.Write("ADL read failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            GC.KeepAlive(mallocCb);
            try { FreeLibrary(hModule); } catch { }
        }
        return result;
    }

    private static T? GetFn<T>(IntPtr hModule, string name) where T : Delegate
    {
        var ptr = GetProcAddress(hModule, name);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private static string AnsiBytesToString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return "";
        var nul = Array.IndexOf(bytes, (byte)0);
        var len = nul >= 0 ? nul : bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, len).Trim();
    }
}
