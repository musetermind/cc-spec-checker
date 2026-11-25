using System.Management;
using System.Text;

namespace SpecChecker.Core.Services;

/// <summary>Detailed system specs via WMI — the engine behind the spec report.</summary>
public static class SpecService
{
    /// <summary>
    /// Gather the full structured model in one pass. This is the single
    /// source of truth: the dashboard reads these records, and GetReport()
    /// renders them into the plain-text report.
    /// </summary>
    public static SpecModel GetModel()
    {
        var (pkg, hot, _) = SensorService.GetCpuTemps();
        var cpuName = "";
        foreach (var s in Query("SELECT Name FROM Win32_Processor"))
            cpuName = s["Name"]?.ToString() ?? "?";
        var cpu = new CpuInfo(cpuName, pkg, hot);

        var dxgiVram = DxgiVram.GetVramByPciId();
        var (vramByPci, vramByName) = GetVramFromRegistry();
        // GPU temperature via NVAPI (NVIDIA driver, no kernel driver needed).
        // ADL (AMD GPU temp) is DISABLED: it crashed the app with
        // 0xc0000005 on real AMD hardware in BOTH build (5) and build (6),
        // and the CRT malloc/free fix did not resolve it. Do NOT re-enable
        // ADL until the crash cause is found AND the code is proven on
        // real AMD hardware. AMD cards show no temp rather than crashing.
        var nvTemps = NvApiTemp.GetGpuTemps();
        var gpus = new List<GpuInfo>();
        var gpuIndex = 0;
        foreach (var s in Query("SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = s["Name"]?.ToString() ?? "?";
            var pciId = GetPciId(s["PNPDeviceID"]?.ToString());
            string? vram = null;
            ulong bytes = 0;
            bool found = (pciId != null && dxgiVram.TryGetValue(pciId, out bytes))
                      || (pciId != null && vramByPci.TryGetValue(pciId, out bytes))
                      || vramByName.TryGetValue(name, out bytes);
            if (found && bytes > 0)
            {
                Logger.Write($"VRAM: '{name}' = {bytes} bytes ({bytes / 1024.0 / 1024:0} MiB) -> {FormatVram(bytes)}.");
                vram = FormatVram(bytes);
            }
            else
            {
                Logger.Write($"VRAM: no DXGI/registry entry for '{name}' (PCI {pciId ?? "?"}); " +
                             $"DXGI saw {dxgiVram.Count} adapter(s), registry had {vramByPci.Count + vramByName.Count} key(s).");
                if (s["AdapterRAM"] is uint b && b > 0 && b < uint.MaxValue)
                    vram = FormatVram(b);
            }
            int? temp = null;
            if (nvTemps.TryGetValue(gpuIndex, out var nv) && nv.tempC >= 0)
                temp = nv.tempC;
            gpus.Add(new GpuInfo(name, vram, temp));
            gpuIndex++;
        }

        var sticks = new List<RamStick>();
        ulong total = 0;
        foreach (var s in Query("SELECT DeviceLocator, Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            var cap = Convert.ToUInt64(s["Capacity"]);
            total += cap;
            uint speed = s["Speed"] is uint sp ? sp : 0;
            sticks.Add(new RamStick(s["DeviceLocator"]?.ToString() ?? "?", cap, speed));
        }
        var ram = new RamInfo(sticks, total);

        var boardMfr = "";
        var boardProduct = "";
        foreach (var s in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
        {
            boardMfr = s["Manufacturer"]?.ToString() ?? "";
            boardProduct = s["Product"]?.ToString() ?? "";
        }
        var bios = "";
        foreach (var s in Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
            bios = s["SMBIOSBIOSVersion"]?.ToString() ?? "";
        var board = new BoardInfo(boardMfr, boardProduct, bios);

        var drives = new List<DriveInfo>();
        foreach (var s in Query("SELECT Model, Size, MediaType FROM Win32_DiskDrive"))
        {
            var size = s["Size"] != null ? $"{Convert.ToUInt64(s["Size"]) / 1024 / 1024 / 1024} GB" : "?";
            drives.Add(new DriveInfo(
                s["Model"]?.ToString() ?? "?",
                size,
                s["MediaType"]?.ToString()?.Trim() ?? ""));
        }

        var health = SmartHealth.GetDriveHealthDetailed();

        var osCaption = "";
        var osArch = "";
        var osBuild = "";
        foreach (var s in Query("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
        {
            osCaption = s["Caption"]?.ToString() ?? "";
            osArch = s["OSArchitecture"]?.ToString() ?? "";
            osBuild = s["BuildNumber"]?.ToString() ?? "";
        }
        var os = new OsInfo(osCaption, osArch, osBuild);

        var monitors = new List<string>();
        foreach (var s in Query("SELECT Name FROM Win32_DesktopMonitor"))
            monitors.Add(s["Name"]?.ToString() ?? "?");

        return new SpecModel(cpu, gpus, ram, board, drives, health, os, monitors);
    }

    /// <summary>
    /// Plain-text report, rendered from the structured model. This is the
    /// same wording as before (snapshot files and clipboard use it), so
    /// the text a user already knows does not change.
    /// </summary>
    public static string GetReport()
    {
        var m = GetModel();
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 70));
        sb.AppendLine("Detailed System Specifications");
        sb.AppendLine(new string('=', 70));

        sb.AppendLine("\nPROCESSOR");
        var temp = m.Cpu.PackageTempC.HasValue ? $" - {m.Cpu.PackageTempC.Value}C package"
                 : " - temp not available (PawnIO driver not installed)";
        sb.AppendLine("  " + m.Cpu.Name + temp);
        if (m.Cpu.HottestCoreTempC.HasValue && m.Cpu.PackageTempC.HasValue
            && m.Cpu.HottestCoreTempC.Value != m.Cpu.PackageTempC.Value)
            sb.AppendLine($"  Hottest core: {m.Cpu.HottestCoreTempC.Value}C");

        sb.AppendLine("\nGRAPHICS CARD");
        foreach (var g in m.Gpus)
        {
            var vram = g.VramText != null ? $" - {g.VramText} VRAM" : "";
            var t = g.TempC.HasValue ? $" - {g.TempC.Value}C" : "";
            sb.AppendLine($"  {g.Name}{vram}{t}");
        }

        sb.AppendLine("\nMEMORY (RAM)");
        foreach (var stick in m.Ram.Sticks)
            sb.AppendLine($"  - [{stick.Slot}] : {stick.CapacityBytes / 1024 / 1024 / 1024}GB @ {stick.SpeedMHz}MHz");
        sb.AppendLine($"  Total Installed: {m.Ram.TotalBytes / 1024 / 1024 / 1024}GB");

        sb.AppendLine("\nMOTHERBOARD");
        sb.AppendLine($"  {m.Board.Manufacturer} {m.Board.Product}");
        sb.AppendLine($"  [BIOS: {m.Board.BiosVersion}]");

        sb.AppendLine("\nSTORAGE DRIVES");
        foreach (var d in m.Drives)
            sb.AppendLine($"  {d.Model} - {d.SizeText} [{d.MediaType}]");

        sb.AppendLine("\nDRIVE HEALTH (SMART)");
        if (m.DriveHealth.Drives.Count == 0)
        {
            var why = m.DriveHealth.Failure switch
            {
                SmartHealth.NoDataReason.AccessDenied => "needs administrator rights",
                SmartHealth.NoDataReason.NotExposed => "not exposed by these drives via WMI (NVMe/USB drives often do not publish it)",
                _ => "could not be read",
            };
            sb.AppendLine($"  SMART data not available ({why})");
        }
        else
        {
            foreach (var h in m.DriveHealth.Drives)
            {
                var status = h.PredictFailure ? "FAILURE PREDICTED" : "OK";
                sb.AppendLine($"  {h.InstanceName} - {status} ({SmartHealth.ReasonLabel(h.Reason)})");
            }
        }

        sb.AppendLine("\nOPERATING SYSTEM");
        sb.AppendLine($"  {m.Os.Caption} ({m.Os.Architecture}) - Build {m.Os.BuildNumber}");

        sb.AppendLine("\nMONITORS");
        foreach (var mon in m.Monitors)
            sb.AppendLine("  " + mon);

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
        // Display VRAM as whole GB (8 GB, 12 GB). DXGI reports the
        // *available* dedicated memory, which is slightly under the
        // card's rated size (e.g. 7.83 GiB for an 8 GB card, because
        // the driver reserves some), so round to the nearest whole GB
        // instead of showing the raw MB figure. The exact byte count
        // still goes to the log for diagnosis.
        const double gib = 1024.0 * 1024 * 1024;
        double gb = bytes / gib;
        double rounded = Math.Round(gb);
        if (Math.Abs(gb - rounded) <= 0.25)
            return $"{rounded:0} GB";
        return $"{gb:0.#} GB";
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
