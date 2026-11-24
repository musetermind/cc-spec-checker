using System.Management;
using System.Text;

namespace SpecChecker.Core.Services;

/// <summary>Detailed system specs via WMI — the engine behind the spec report.</summary>
public static class SpecService
{
    public static string GetReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 70));
        sb.AppendLine("Detailed System Specifications");
        sb.AppendLine(new string('=', 70));

        sb.AppendLine("\nPROCESSOR");
        foreach (var s in Query("SELECT Name FROM Win32_Processor"))
            sb.AppendLine("  " + s["Name"]);

        sb.AppendLine("\nGRAPHICS CARD");
        // VRAM lookup order: DXGI (exact, all vendors) -> driver registry ->
        // WMI AdapterRAM (32-bit, only trustworthy below 4GB).
        var dxgiVram = DxgiVram.GetVramByPciId();
        var (vramByPci, vramByName) = GetVramFromRegistry();
        // GPU temperature via NVAPI (NVIDIA driver, no kernel driver needed).
        var nvTemps = NvApiTemp.GetGpuTemps();
        var gpuIndex = 0;
        foreach (var s in Query("SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = s["Name"]?.ToString() ?? "?";
            var pciId = GetPciId(s["PNPDeviceID"]?.ToString());
            var vram = "";
            ulong bytes = 0;
            bool found = (pciId != null && dxgiVram.TryGetValue(pciId, out bytes))
                      || (pciId != null && vramByPci.TryGetValue(pciId, out bytes))
                      || vramByName.TryGetValue(name, out bytes);
            if (found && bytes > 0)
            {
                vram = $" - {FormatVram(bytes)} VRAM";
            }
            else
            {
                Logger.Write($"VRAM: no DXGI/registry entry for '{name}' (PCI {pciId ?? "?"}); " +
                             $"DXGI saw {dxgiVram.Count} adapter(s), registry had {vramByPci.Count + vramByName.Count} key(s).");
                if (s["AdapterRAM"] is uint b && b > 0 && b < uint.MaxValue)
                {
                    // Only trustworthy below 4GB; at uint.MaxValue WMI overflowed.
                    vram = $" - {b / 1024 / 1024}MB VRAM";
                }
            }
            var temp = "";
            if (nvTemps.TryGetValue(gpuIndex, out var nv) && nv.tempC >= 0)
                temp = $" - {nv.tempC}C";
            sb.AppendLine($"  {name}{vram}{temp}");
            gpuIndex++;
        }

        sb.AppendLine("\nMEMORY (RAM)");
        ulong total = 0;
        foreach (var s in Query("SELECT DeviceLocator, Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            var cap = Convert.ToUInt64(s["Capacity"]);
            total += cap;
            sb.AppendLine($"  - [{s["DeviceLocator"]}] : {cap / 1024 / 1024 / 1024}GB @ {s["Speed"]}MHz");
        }
        sb.AppendLine($"  Total Installed: {total / 1024 / 1024 / 1024}GB");

        sb.AppendLine("\nMOTHERBOARD");
        foreach (var s in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            sb.AppendLine($"  {s["Manufacturer"]} {s["Product"]}");
        foreach (var s in Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
            sb.AppendLine($"  [BIOS: {s["SMBIOSBIOSVersion"]}]");

        sb.AppendLine("\nSTORAGE DRIVES");
        foreach (var s in Query("SELECT Model, Size, MediaType FROM Win32_DiskDrive"))
        {
            var size = s["Size"] != null ? $"{Convert.ToUInt64(s["Size"]) / 1024 / 1024 / 1024} GB" : "?";
            sb.AppendLine($"  {s["Model"]} - {size} [{s["MediaType"]?.ToString()?.Trim()}]");
        }

        sb.AppendLine("\nOPERATING SYSTEM");
        foreach (var s in Query("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
            sb.AppendLine($"  {s["Caption"]} ({s["OSArchitecture"]}) - Build {s["BuildNumber"]}");

        sb.AppendLine("\nMONITORS");
        foreach (var s in Query("SELECT Name FROM Win32_DesktopMonitor"))
            sb.AppendLine("  " + s["Name"]);

        sb.AppendLine(new string('=', 70));
        return sb.ToString();
    }

    /// <summary>
    /// Real VRAM per GPU from the driver registry (REG_QWORD bytes).
    /// Win32_VideoController.AdapterRAM is a 32-bit field that caps at 4GB,
    /// which is why every modern card showed 4095/4096MB.
    /// Returns two maps: by PCI VEN/DEV id (most reliable) and by driver
    /// description (fallback, matches the WMI Name).
    /// </summary>
    private static (Dictionary<string, ulong> byPci, Dictionary<string, ulong> byName) GetVramFromRegistry()
    {
        var byPci = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-4e8b-bcfd-545a46e2558b}");
            if (cls == null) return (byPci, byName);
            foreach (var sub in cls.GetSubKeyNames())
            {
                using var k = cls.OpenSubKey(sub);
                if (k == null) continue;
                // MemorySize may live directly on the device key or under HardwareInformation.
                object? mem = k.GetValue("MemorySize")
                           ?? k.OpenSubKey("HardwareInformation")?.GetValue("MemorySize");
                if (mem == null) continue;
                ulong bytes = mem is long l ? (ulong)l : Convert.ToUInt64(mem);
                if (bytes == 0) continue;
                var pciId = GetPciId(k.GetValue("MatchingDeviceId") as string);
                if (pciId != null) byPci[pciId] = bytes;
                var desc = k.GetValue("DriverDesc") as string;
                if (!string.IsNullOrEmpty(desc)) byName[desc] = bytes;
            }
        }
        catch (Exception ex)
        {
            Logger.Write("VRAM registry read failed: " + ex.Message);
        }
        return (byPci, byName);
    }

    /// <summary>Extracts "VVVV&DDDD" (VEN/DEV) from a PCI device id string.</summary>
    private static string? GetPciId(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            deviceId, @"VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})");
        return m.Success
            ? (m.Groups[1].Value + "&" + m.Groups[2].Value).ToUpperInvariant()
            : null;
    }

    private static string FormatVram(ulong bytes)
    {
        const ulong gib = 1024UL * 1024 * 1024;
        if (bytes >= gib && bytes % gib == 0)
            return $"{bytes / gib}GB";
        return $"{bytes / 1024 / 1024}MB";
    }

    private static List<ManagementObject> Query(string wql)
    {
        var list = new List<ManagementObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject mo in searcher.Get()) list.Add(mo);
        }
        catch (Exception ex)
        {
            Logger.Write("WMI query failed: " + ex.Message);
        }
        return list;
    }

    /// <summary>Board manufacturer, for driver-hub hints.</summary>
    public static string GetBoardManufacturer()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard");
            foreach (ManagementObject mo in searcher.Get())
                return mo["Manufacturer"]?.ToString() ?? "";
        }
        catch { }
        return "";
    }
}
