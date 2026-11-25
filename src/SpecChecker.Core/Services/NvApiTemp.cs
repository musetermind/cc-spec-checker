using System.Runtime.InteropServices;

namespace SpecChecker.Core.Services;

/// <summary>
/// NVIDIA GPU temperature (and memory, as a cross-check) via NVAPI — the
/// driver API, so no kernel driver is needed and it works unelevated.
/// nvapi64.dll ships with the NVIDIA driver. Functions are resolved by
/// magic offset through NvAPI_QueryInterface, the same manual pattern as
/// DxgiVram (no vendor SDK, no static link).
///
/// Offsets (NVAPI):
///   NvAPI_Initialize            0x0150E828
///   NvAPI_EnumPhysicalGPUs      0xE5AC921F
///   NvAPI_GPU_GetThermalSettings 0xE3640A56
///   NvAPI_GPU_GetMemoryInfoEx   0x774AA982
///   NvAPI_Unload                0xD22BDD7E
/// </summary>
internal static class NvApiTemp
{
    private const uint NVAPI_OK = 0;
    private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
    private const int NVAPI_MAX_THERMAL_SENSORS_PER_GPU = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryInterfaceFn(uint offset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint InitializeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint EnumPhysicalGPUsFn([Out] IntPtr[] gpuHandles, out int gpuCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetThermalSettingsFn(IntPtr gpuHandle, int sensorIndex, ref GpuThermalSettings settings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetMemoryInfoExFn(IntPtr gpuHandle, ref GpuMemoryInfoEx info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UnloadFn();

    // NV_GPU_THERMAL_SENSOR: controller + 4 temperatures (all int32).
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuThermalSensor
    {
        public int Controller;
        public int DefaultMinTemp;
        public int DefaultMaxTemp;
        public int CurrentTemp;
        public int Target;
    }

    // NV_GPU_THERMAL_SETTINGS: version + count + 3 sensors.
    // Version = sizeof(struct) | (1 << 16); struct is 8 + 3*20 = 68 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuThermalSettings
    {
        public uint Version;
        public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NVAPI_MAX_THERMAL_SENSORS_PER_GPU)]
        public GpuThermalSensor[] Sensor;
    }

    // NV_GPU_MEMORY_INFO_EX (KB). Version = sizeof(struct) | (2 << 16).
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuMemoryInfoEx
    {
        public uint Version;
        public uint DedicatedVideoMemory;
        public uint AvailableDedicatedVideoMemory;
        public uint SystemVideoMemory;
        public uint SharedSystemMemory;
        public uint CurAvailableDedicatedVideoMemory;
    }

    private static uint ThermalSettingsVersion =>
        (uint)(Marshal.SizeOf<GpuThermalSettings>() | (1 << 16));

    private static uint MemoryInfoExVersion =>
        (uint)(Marshal.SizeOf<GpuMemoryInfoEx>() | (2 << 16));

    /// <summary>GPU temperature in °C per physical GPU handle index, plus the
    /// dedicated VRAM (KB) as a cross-check against the DXGI figure.
    /// Returns an empty map when NVAPI is unavailable (no NVIDIA GPU/driver).
    /// The thermal sensors are ordered GPU (0), memory (1), hotspot (2) on
    /// modern NVIDIA cards — the same layout tools like HWiNFO read. The
    /// hotspot is only reported when a third sensor exists (Count >= 3);
    /// all three raw values go to the log so the mapping is verifiable.</summary>
    public static Dictionary<int, (int tempC, int? hotspotC, ulong vramBytes)> GetGpuTemps()
    {
        var result = new Dictionary<int, (int, int?, ulong)>();
        var hModule = LoadLibrary("nvapi64.dll");
        if (hModule == IntPtr.Zero)
        {
            Logger.Write("NVAPI: nvapi64.dll not present (no NVIDIA driver).");
            return result;
        }
        try
        {
            var queryPtr = GetProcAddress(hModule, "nvapi_QueryInterface");
            if (queryPtr == IntPtr.Zero)
            {
                Logger.Write("NVAPI: nvapi_QueryInterface not found.");
                return result;
            }
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(queryPtr);

            var init = GetFn<InitializeFn>(query, 0x0150E828);
            var enumGpus = GetFn<EnumPhysicalGPUsFn>(query, 0xE5AC921F);
            var getThermal = GetFn<GetThermalSettingsFn>(query, 0xE3640A56);
            var getMem = GetFn<GetMemoryInfoExFn>(query, 0x774AA982);
            var unload = GetFn<UnloadFn>(query, 0xD22BDD7E);
            if (init == null || enumGpus == null || getThermal == null)
            {
                Logger.Write("NVAPI: could not resolve core functions.");
                return result;
            }

            if (init() != NVAPI_OK)
            {
                Logger.Write("NVAPI: NvAPI_Initialize failed.");
                return result;
            }
            try
            {
                var handles = new IntPtr[NVAPI_MAX_PHYSICAL_GPUS];
                if (enumGpus(handles, out var count) != NVAPI_OK || count <= 0)
                {
                    Logger.Write("NVAPI: no physical GPUs enumerated.");
                    return result;
                }
                for (var i = 0; i < count; i++)
                {
                    var temp = -1;
                    int? hotspot = null;
                    var settings = new GpuThermalSettings
                    {
                        Version = ThermalSettingsVersion,
                        Sensor = new GpuThermalSensor[NVAPI_MAX_THERMAL_SENSORS_PER_GPU],
                    };
                    if (getThermal(handles[i], 0, ref settings) == NVAPI_OK && settings.Count > 0)
                    {
                        temp = settings.Sensor[0].CurrentTemp;
                        if (settings.Count >= 3)
                            hotspot = settings.Sensor[2].CurrentTemp;
                        var all = string.Join(", ", settings.Sensor.Take((int)settings.Count).Select(s => s.CurrentTemp + "C"));
                        Logger.Write($"NVAPI: GPU {i}: sensors [{all}] (GPU, memory, hotspot).");
                    }
                    ulong vram = 0;
                    if (getMem != null)
                    {
                        var mem = new GpuMemoryInfoEx { Version = MemoryInfoExVersion };
                        if (getMem(handles[i], ref mem) == NVAPI_OK)
                            vram = (ulong)mem.DedicatedVideoMemory * 1024;
                    }
                    Logger.Write($"NVAPI: GPU {i}: temp={temp}C, hotspot={hotspot?.ToString() ?? "?"}C, VRAM={vram} bytes (cross-check).");
                    result[i] = (temp, hotspot, vram);
                }
            }
            finally
            {
                try { unload?.Invoke(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Write("NVAPI read failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { FreeLibrary(hModule); } catch { }
        }
        return result;
    }

    private static T? GetFn<T>(QueryInterfaceFn query, uint offset) where T : Delegate
    {
        var ptr = query(offset);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}
