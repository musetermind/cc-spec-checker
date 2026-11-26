using System.Management;
using System.Text;

namespace SpecChecker.Core.Services;

/// <summary>Detailed system specs via WMI — the engine behind the spec report.</summary>
public static class SpecService
{
    /// <summary>Close the sensor engine so the next read opens a fresh
    /// one. Used after the PawnIO driver is installed while the app is
    /// running — the old engine was opened without the driver.</summary>
    public static void ResetSensors() => SensorService.Reset();

    /// <summary>
    /// Gather the full structured model in one pass. This is the single
    /// source of truth: the dashboard reads these records, and GetReport()
    /// renders them into the plain-text report.
    /// </summary>
    public static SpecModel GetModel()
    {
        var (pkg, hot, boardTemp) = SensorService.GetCpuTemps();
        var cpuName = "";
        var cores = 0;
        var threads = 0;
        uint l2 = 0, l3 = 0;
        foreach (var s in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, L2CacheSize, L3CacheSize FROM Win32_Processor"))
        {
            cpuName = s["Name"]?.ToString() ?? "?";
            cores = s["NumberOfCores"] is uint c ? (int)c : 0;
            threads = s["NumberOfLogicalProcessors"] is uint t ? (int)t : 0;
            l2 = s["L2CacheSize"] is uint l2v ? l2v : 0;
            l3 = s["L3CacheSize"] is uint l3v ? l3v : 0;
        }
        // No max clock: WMI's MaxClockSpeed is the base clock (3.2 GHz
        // for a 14900K), not the boost, so showing it as "max" was
        // wrong (Sithu, build 11). The field is gone entirely.
        var cpu = new CpuInfo(cpuName, cores, threads, l2, l3, pkg, hot);

        var dxgiVram = DxgiVram.GetVramByPciId();
        var (vramByPci, vramByName) = GetVramFromRegistry();
        // GPU temperature via NVAPI (NVIDIA driver, no kernel driver needed).
        // ADL (AMD GPU temp) is DISABLED: it crashed the app with
        // 0xc0000005 on real AMD hardware in BOTH build (5) and build (6),
        // and the CRT malloc/free fix did not resolve it. Do NOT re-enable
        // ADL until the crash cause is found AND the code is proven on
        // real AMD hardware. AMD cards show no temp rather than crashing.
        var nvTemps = NvApiTemp.GetGpuTemps();
        // NVAPI temperatures are matched to cards by PCI key first
        // (vendor, device and subsystem — the exact card, no ordering
        // and no size involved), and by NVIDIA order as the fallback:
        // WMI lists the integrated GPU first on laptops, so a WMI index
        // is not an NVAPI index — but the i-th NVIDIA controller in WMI
        // order is the i-th NVAPI GPU. VRAM size was tried as the key
        // first and dropped: NVAPI's memory query returns 0 on this
        // hardware, and even when it works DXGI reports less than the
        // full VRAM (the driver holds back ~174 MiB on an 8 GB card),
        // so exact VRAM matching can never be trusted. Two identical
        // cards share one PCI key, so each key holds a queue and every
        // controller takes the next temperature from it.
        var nvByPci = new Dictionary<string, Queue<(int tempC, int? hotspotC)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var nv in nvTemps.Values)
            if (nv.tempC >= 0 && nv.pciKey != null)
            {
                if (!nvByPci.TryGetValue(nv.pciKey, out var q))
                    nvByPci[nv.pciKey] = q = new Queue<(int, int?)>();
                q.Enqueue((nv.tempC, nv.hotspotC));
            }
        var gpus = new List<GpuInfo>();
        var gpuVramBytes = new List<ulong>(); // parallel to gpus, for sorting
        var controllers = Query("SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
        var nvidiaOrdinal = 0; // NVIDIA controllers seen so far, in WMI order
        foreach (var s in controllers)
        {
            var name = s["Name"]?.ToString() ?? "?";
            var pnpId = s["PNPDeviceID"]?.ToString();
            var pciId = GetPciId(pnpId);
            var brand = GetGpuBrand(pnpId);
            string? vram = null;
            ulong bytes = 0, sharedBytes = 0;
            if (pciId != null && dxgiVram.TryGetValue(pciId, out var dx))
            {
                bytes = dx.dedicated;
                sharedBytes = dx.shared;
            }
            else if (pciId != null && vramByPci.TryGetValue(pciId, out bytes))
            {
                // Registry fallback: no shared-memory figure.
            }
            else if (!vramByName.TryGetValue(name, out bytes))
            {
                bytes = 0;
            }
            if (bytes > 0)
            {
                Logger.Write($"VRAM: '{name}' = {bytes} bytes ({bytes / 1024.0 / 1024:0} MiB) -> {FormatVram(bytes)}"
                    + (sharedBytes > 0 ? $" + {sharedBytes / 1024.0 / 1024:0} MiB shared." : "."));
                // Integrated GPU: tiny dedicated memory, large shared pool —
                // label both, so a laptop never reads "0 GB" or a bare "128 MB".
                vram = bytes <= 512UL * 1024 * 1024 && sharedBytes > 0
                    ? $"{FormatVram(bytes)} Dedicated + {FormatVram(sharedBytes)} Shared"
                    : FormatVram(bytes);
            }
            else
            {
                Logger.Write($"VRAM: no DXGI/registry entry for '{name}' (PCI {pciId ?? "?"}); " +
                             $"DXGI saw {dxgiVram.Count} adapter(s), registry had {vramByPci.Count + vramByName.Count} key(s).");
                if (s["AdapterRAM"] is uint b && b > 0 && b < uint.MaxValue)
                {
                    bytes = b;
                    vram = FormatVram(b);
                }
            }
            // Integrated graphics has no board partner: the subsystem
            // vendor would only name the board maker (ASUS on the bench),
            // which reads wrong next to a name that already says Intel
            // or AMD. Same test as the shared-memory label — tiny
            // dedicated memory plus a shared pool.
            if (bytes > 0 && bytes <= 512UL * 1024 * 1024 && sharedBytes > 0)
                brand = null;
            int? temp = null;
            int? hotspot = null;
            var isNvidia = pnpId != null && pnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase);
            var pciKey = GetPciKey(pnpId);
            if (pciKey != null && nvByPci.TryGetValue(pciKey, out var q) && q.Count > 0)
            {
                var t = q.Dequeue();
                temp = t.tempC;
                hotspot = t.hotspotC;
            }
            else if (isNvidia && nvTemps.TryGetValue(nvidiaOrdinal, out var nv) && nv.tempC >= 0)
            {
                temp = nv.tempC;
                hotspot = nv.hotspotC;
            }
            else if (controllers.Count == 1 && nvTemps.TryGetValue(0, out var nv0) && nv0.tempC >= 0)
            {
                // Single-GPU machine: the indices agree, as before.
                temp = nv0.tempC;
                hotspot = nv0.hotspotC;
            }
            if (isNvidia) nvidiaOrdinal++;
            gpus.Add(new GpuInfo(name, vram, temp, hotspot, brand));
            gpuVramBytes.Add(bytes);
        }
        // Discrete card first: WMI lists the integrated GPU first on
        // laptops and Ryzen desktops with an iGPU, so sort by VRAM
        // descending (stable — equal sizes keep WMI order).
        gpus = gpus.Zip(gpuVramBytes, (g, b) => (g, b)).OrderByDescending(x => x.b).Select(x => x.g).ToList();

        var sticks = new List<RamStick>();
        ulong total = 0;
        var memType = "";
        // 0.16 asked WMI for CASLatency as well — that property does NOT
        // exist on Win32_PhysicalMemory (Microsoft's class definition
        // lists 36 properties; CASLatency is not one), so the whole query
        // failed and the app saw zero sticks ("No RAM sticks found" on a
        // machine with 64 GB). The running CL lives in the SPD EEPROM on
        // the stick and is readable only via the SMBus with a driver;
        // WMI has no such field, so no CL is shown rather than a guessed
        // one. (InterleaveDataDepth was asked for in 0.16-1.8 for the
        // channel mode; the 1.9 rule is by stick count, so it is not
        // asked for any more.)
        foreach (var s in Query("SELECT DeviceLocator, Capacity, Speed, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory"))
        {
            var cap = Convert.ToUInt64(s["Capacity"]);
            total += cap;
            uint speed = s["Speed"] is uint sp ? sp : 0;
            var vendor = s["Manufacturer"]?.ToString()?.Trim() ?? "";
            sticks.Add(new RamStick(s["DeviceLocator"]?.ToString() ?? "?", cap, speed, vendor));
            if (string.IsNullOrEmpty(memType) && s["SMBIOSMemoryType"] is uint mt)
                memType = MemoryTypeName(mt, speed);
        }
        var ram = new RamInfo(sticks, total, memType, SensorService.GetRamTemp(), ChannelModeName(sticks.Count));

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
        var board = new BoardInfo(boardMfr, boardProduct, bios, boardTemp);

        // Real drive type (SSD vs HDD): WMI's Win32_DiskDrive.MediaType
        // says "Fixed hard disk media" for EVERY fixed disk — SSD or
        // spinning, it is the same string, so it cannot tell them apart
        // (Sithu, build 12). MSFT_PhysicalDisk (root\Microsoft\Windows
        // \Storage) reports the real MediaType (3=HDD, 4=SSD) and BusType
        // (17=NVMe, 11=SATA, 7=USB). Match by model name; no match or a
        // failed query = keep the WMI string, never a guessed type.
        var physDisks = QueryStorage("SELECT FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk");
        // Drive letters + used/free space (Sithu, 0.15): map each
        // physical drive to its volumes before the drive loop.
        var volumesByDrive = GetVolumesByDrive();
        var drives = new List<DriveInfo>();
        foreach (var s in Query("SELECT Model, Size, MediaType, DeviceID FROM Win32_DiskDrive"))
        {
            var size = s["Size"] != null ? $"{Convert.ToUInt64(s["Size"]) / 1024 / 1024 / 1024} GB" : "?";
            var model = s["Model"]?.ToString() ?? "?";
            var mediaType = s["MediaType"]?.ToString()?.Trim() ?? "";
            foreach (var p in physDisks)
            {
                var friendly = p["FriendlyName"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(friendly)) continue;
                if (model.Contains(friendly, StringComparison.OrdinalIgnoreCase)
                    || friendly.Contains(model, StringComparison.OrdinalIgnoreCase))
                {
                    // MediaType and BusType are UInt16 in MSFT_PhysicalDisk,
                    // not UInt32 — 'is uint' never matched, so both read as
                    // 0 and every drive fell back to the WMI string (the
                    // build-12 bug: "Fixed hard disk media" everywhere).
                    uint mt = p["MediaType"] != null ? Convert.ToUInt32(p["MediaType"]) : 0;
                    uint bt = p["BusType"] != null ? Convert.ToUInt32(p["BusType"]) : 0;
                    mediaType = DriveTypeLabel(mt, bt, mediaType);
                    Logger.Write($"Drive type: '{model}' MSFT MediaType={mt}, BusType={bt} -> {mediaType}.");
                    break;
                }
            }
            // No drive temp: LHM's storage sensor proved unreliable (94C
            // for a drive HD Sentinel reads at 39C), so none is shown.
            // Volumes (letters + used/free) come from the mapping above;
            // a drive with no mounted volume gets an empty list.
            var deviceId = s["DeviceID"]?.ToString() ?? "";
            var volumes = deviceId != "" && volumesByDrive.TryGetValue(deviceId, out var v)
                ? v : new List<VolumeInfo>();
            drives.Add(new DriveInfo(
                model,
                size,
                mediaType,
                volumes));
        }

        var osCaption = "";
        var osArch = "";
        var osBuild = "";
        foreach (var s in Query("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
        {
            osCaption = s["Caption"]?.ToString() ?? "";
            osArch = s["OSArchitecture"]?.ToString() ?? "";
            osBuild = s["BuildNumber"]?.ToString() ?? "";
        }
        var os = new OsInfo(osCaption, osArch, osBuild, IsWindowsActivated());

        var monitors = new List<string>();
        foreach (var s in Query("SELECT Name FROM Win32_DesktopMonitor"))
            monitors.Add(s["Name"]?.ToString() ?? "?");

        return new SpecModel(cpu, gpus, ram, board, drives, os, monitors);
    }

    /// <summary>Cache sizes as "L2 32MB · L3 36MB" (only the caches that
    /// report a size — a zero is a missing reading, not a 0MB cache).</summary>
    public static string FormatCache(uint l2KB, uint l3KB)
    {
        var parts = new List<string>();
        if (l2KB > 0) parts.Add($"L2 {l2KB / 1024}MB");
        if (l3KB > 0) parts.Add($"L3 {l3KB / 1024}MB");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// SMBIOS memory type code to the DDR generation label. The codes are
    /// the raw SMBIOS Memory Device Type values (Win32_PhysicalMemory
    /// SMBIOSMemoryType): 18=DDR, 19=DDR2, 20=DDR2 FB-DIMM, 24=DDR3,
    /// 25=FBD2, 26=DDR4, 27=LPDDR, 28=LPDDR2, 29=LPDDR3, 30=LPDDR4,
    /// 31=Logical non-volatile device, 32=HBM, 33=HBM2, 34=DDR5,
    /// 35=LPDDR5. An earlier version labelled these with the older WMI
    /// MemoryType numbering, which put DDR5 at 33 and LPDDR5 at 34 — so
    /// every DDR5 machine read as LPDDR5, and DDR3 read as FBD2. The raw
    /// code and speed go to the log so the mapping is auditable.
    /// </summary>
    private static string MemoryTypeName(uint code, uint speedMHz)
    {
        var label = code switch
        {
            18 => "DDR",
            19 => "DDR2",
            20 => "DDR2 FB-DIMM",
            24 => "DDR3",
            25 => "FBD2",
            26 => "DDR4",
            27 => "LPDDR",
            28 => "LPDDR2",
            29 => "LPDDR3",
            30 => "LPDDR4",
            31 => "Logical non-volatile device",
            32 => "HBM",
            33 => "HBM2",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => $"Unknown (type {code})",
        };
        Logger.Write($"RAM type: SMBIOSMemoryType={code}, Speed={speedMHz}MHz -> {label}.");
        return label;
    }

    /// <summary>
    /// The board-partner brand (Zotac, ASUS, MSI...) from the PCI
    /// subsystem vendor ID in the device's PNPDeviceID
    /// ("PCI\VEN_10DE&DEV_2484&SUBSYS_148219DA&REV_A1\..."). The SUBSYS
    /// value is 8 hex digits: the first 4 are the subsystem device, the
    /// LAST 4 are the subsystem vendor (19DA = Zotac). WMI's
    /// Win32_VideoController has no brand field, and the chip vendor
    /// (VEN_10DE = NVIDIA, VEN_1002 = AMD) is the chip maker, not the
    /// board maker — so this is the only no-driver way to tell a Zotac
    /// from an ASUS. Unknown vendor, a zero SUBSYS, or a missing
    /// PNPDeviceID = null: no brand shown rather than a guessed one.
    /// The map covers the common partners and OEMs; the raw SUBSYS goes
    /// to the log so an unmapped vendor can be added later.
    /// </summary>
    private static string? GetGpuBrand(string? pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(pnpDeviceId, @"SUBSYS_([0-9A-Fa-f]{8})");
        if (!m.Success) return null;
        var subsys = m.Groups[1].Value;
        var vendor = Convert.ToUInt32(subsys.Substring(4, 4), 16);
        var brand = vendor switch
        {
            0x1002 => "AMD",      // reference board
            0x1028 => "Dell",
            0x103C => "HP",
            0x1043 => "ASUS",
            0x10B0 => "Gainward",
            0x10DE => "NVIDIA",  // Founders Edition / reference
            0x1458 => "Gigabyte",
            0x1462 => "MSI",
            0x148C => "PowerColor",
            0x1569 => "Palit",
            0x1787 => "XFX",
            0x17AA => "Lenovo",
            0x1849 => "ASRock",
            0x196E => "PNY",
            0x19DA => "Zotac",
            0x1DA2 => "Sapphire",
            0x3842 => "EVGA",
            0x7377 => "Colorful",
            _ => null,
        };
        Logger.Write($"GPU brand: SUBSYS={subsys}, vendor=0x{vendor:X4} -> {brand ?? "unknown"}.");
        return brand;
    }

    /// <summary>
    /// The channel mode to show on the MEMORY summary line, by stick
    /// count (Sithu, 1.9): 2 or more sticks is "Dual Channel", 1 stick
    /// shows nothing. The 1.8 rule read WMI's InterleaveDataDepth and
    /// showed Single when it reported 1 — the bench reported 1 with 2
    /// sticks in it, so the depth rule is dropped; the count is what
    /// Sithu wants shown.
    /// </summary>
    private static string? ChannelModeName(int stickCount)
    {
        string? mode = stickCount >= 2 ? "Dual Channel" : null;
        Logger.Write($"RAM channel: sticks={stickCount} -> {mode ?? "not shown"}.");
        return mode;
    }

    /// <summary>
    /// The real drive type from MSFT_PhysicalDisk: MediaType 4=SSD,
    /// 3=HDD, 5=SCM; BusType 17=NVMe, 11=SATA, 7=USB, 10=SAS, 1=SCSI.
    /// Labels match Task Manager ("SSD (NVMe)", "HDD (SATA)",
    /// "Removable (USB)"). Anything else keeps the WMI string (the
    /// fallback) so an unknown type is never mislabeled.
    /// </summary>
    private static string DriveTypeLabel(uint mediaType, uint busType, string fallback)
    {
        var bus = busType switch
        {
            17 => "NVMe",
            11 => "SATA",
            7 => "USB",
            10 => "SAS",
            1 => "SCSI",
            _ => null,
        };
        return mediaType switch
        {
            4 => bus != null ? $"SSD ({bus})" : "SSD",
            3 => bus != null ? $"HDD ({bus})" : "HDD",
            5 => "SCM",
            _ => busType == 7 ? "Removable (USB)" : fallback,
        };
    }

    /// <summary>
    /// Map each physical drive (Win32_DiskDrive.DeviceID, e.g.
    /// "\\.\PHYSICALDRIVE0") to its volumes (letters + sizes), via the
    /// DiskDriveToDiskPartition and LogicalDiskToPartition associators,
    /// joined in memory by parsing the DeviceID out of each reference
    /// path. Network drives and CD-ROMs have no physical disk here, so
    /// they never appear. A failure just means no volume lines — the
    /// drive list itself is unaffected.
    /// </summary>
    private static Dictionary<string, List<VolumeInfo>> GetVolumesByDrive()
    {
        var result = new Dictionary<string, List<VolumeInfo>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Logical disks with sizes (skip network + optical: no Size, or DriveType 4/5).
            var logical = new Dictionary<string, (ulong Total, ulong Free)>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Query("SELECT DeviceID, Size, FreeSpace, DriveType FROM Win32_LogicalDisk"))
            {
                var letter = s["DeviceID"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(letter)) continue;
                if (s["DriveType"] is uint dt && (dt == 4 || dt == 5)) continue; // network, CD-ROM
                if (s["Size"] == null || s["FreeSpace"] == null) continue;
                logical[letter] = (Convert.ToUInt64(s["Size"]), Convert.ToUInt64(s["FreeSpace"]));
            }
            if (logical.Count == 0) return result;

            // Partition -> drive, then letter -> partition, joined on DeviceID.
            var partitionToDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Query("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
            {
                var drive = DeviceIdFromPath(s["Antecedent"]?.ToString());
                var part = DeviceIdFromPath(s["Dependent"]?.ToString());
                if (drive != null && part != null) partitionToDrive[part] = drive;
            }
            foreach (var s in Query("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            {
                var part = DeviceIdFromPath(s["Antecedent"]?.ToString());
                var letter = DeviceIdFromPath(s["Dependent"]?.ToString());
                if (part == null || letter == null) continue;
                if (!partitionToDrive.TryGetValue(part, out var drive)) continue;
                if (!logical.TryGetValue(letter, out var sizes)) continue;
                if (!result.TryGetValue(drive, out var list)) { list = new List<VolumeInfo>(); result[drive] = list; }
                list.Add(new VolumeInfo(letter, sizes.Total, sizes.Free));
            }
            // Sort each drive's volumes by letter (C: before D:).
            foreach (var list in result.Values)
                list.Sort((a, b) => string.Compare(a.Letter, b.Letter, StringComparison.OrdinalIgnoreCase));
            Logger.Write($"Volumes: mapped {result.Count} drive(s) to letters.");
            foreach (var (drive, list) in result)
                Logger.Write($"Volumes: {drive} -> {string.Join(", ", list.Select(v => v.Letter))}.");
        }
        catch (Exception ex)
        {
            Logger.Write("Volume mapping failed: " + ex.Message);
        }
        return result;
    }

    /// <summary>Pull the DeviceID value out of a WMI reference path
    /// (e.g. ...Win32_DiskDrive.DeviceID="\\.\PHYSICALDRIVE0"). The
    /// reference may arrive as a string or a ManagementPath — ToString()
    /// gives the path either way. WMI object-path key values escape
    /// backslashes by doubling them: the drive's DeviceID is
    /// "\\.\PHYSICALDRIVE0", but the reference path carries it as
    /// "\\\\.\\PHYSICALDRIVE0". Without unescaping, the join key never
    /// equals the drive's DeviceID and no volume is ever mapped — the
    /// 0.15 bug: the volume lines never appeared on the bench even
    /// though every drive has a letter.</summary>
    private static string? DeviceIdFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(path, @"DeviceID=""([^""]+)""");
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
    }

    /// <summary>
    /// Query the Storage WMI namespace (MSFT_PhysicalDisk lives there,
    /// not in root\cimv2). Readable without admin; a failure just means
    /// the caller falls back to the Win32_DiskDrive strings.
    /// </summary>
    private static List<ManagementObject> QueryStorage(string wql)
    {
        var list = new List<ManagementObject>();
        try
        {
            var scope = new ManagementScope(@"root\Microsoft\Windows\Storage");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
            foreach (ManagementObject mo in searcher.Get()) list.Add(mo);
        }
        catch (Exception ex)
        {
            Logger.Write("Storage WMI query failed: " + ex.Message);
        }
        return list;
    }

    /// <summary>True when this process is running with administrator rights.</summary>
    public static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Whether Windows is activated: LicenseStatus 1 (Licensed) on the
    /// Windows SoftwareLicensingProduct. Any other status = not activated.
    /// </summary>
    private static bool IsWindowsActivated()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["LicenseStatus"] is uint status && status == 1)
                    return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Write("Activation check failed: " + ex.Message);
        }
        return false;
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
        sb.AppendLine("  " + m.Cpu.Name);
        if (m.Cpu.Cores > 0)
            sb.AppendLine($"  Cores: {m.Cpu.Cores} · Threads: {m.Cpu.Threads}");
        var cache = FormatCache(m.Cpu.L2CacheKB, m.Cpu.L3CacheKB);
        if (!string.IsNullOrEmpty(cache))
            sb.AppendLine($"  Cache: {cache}");
        // CPU temp: name the actual blocker. No driver — the install
        // is offered on the Processor card. Driver there but
        // unelevated — restart as admin. Driver there, elevated, still
        // no temp — something else is wrong; do not blame the driver.
        var temp = m.Cpu.PackageTempC.HasValue ? $"  Package temp: {m.Cpu.PackageTempC.Value}C"
                 : !PawnIoDriver.IsInstalled ? "  Package temp: not available (PawnIO driver not installed — install it from the Processor card)"
                 : IsElevated() ? "  Package temp: not available"
                 : "  Package temp: Restart as Admin to check CPU temperature";
        sb.AppendLine(temp);
        if (m.Cpu.HottestCoreTempC.HasValue)
            sb.AppendLine($"  Hottest core: {m.Cpu.HottestCoreTempC.Value}C");

        sb.AppendLine("\nGRAPHICS CARD");
        foreach (var g in m.Gpus)
        {
            var brand = g.Brand != null ? $" · {g.Brand}" : "";
            var vram = g.VramText != null ? $" - {g.VramText} VRAM" : "";
            var t = g.TempC.HasValue ? $" - {g.TempC.Value}C" : "";
            var hs = g.HotspotTempC.HasValue ? $" (hotspot {g.HotspotTempC.Value}C)" : "";
            sb.AppendLine($"  {g.Name}{brand}{vram}{t}{hs}");
        }

        sb.AppendLine("\nMEMORY (RAM)");
        if (!string.IsNullOrEmpty(m.Ram.MemoryType))
            sb.AppendLine($"  Type: {m.Ram.MemoryType}");
        // Summary line mirrors the card: channel mode (Sithu, 0.16).
        // No CL: there is no WMI field for the running CAS latency (see
        // the RAM query comment — 0.16 broke on a property that does not
        // exist), so none is shown rather than a guessed one.
        if (m.Ram.ChannelMode != null)
            sb.AppendLine($"  {m.Ram.ChannelMode}");
        if (m.Ram.TempC.HasValue)
            sb.AppendLine($"  Module temp: {m.Ram.TempC.Value}C");
        foreach (var stick in m.Ram.Sticks)
        {
            var vendor = string.IsNullOrWhiteSpace(stick.Manufacturer) ? "" : $" {stick.Manufacturer}";
            sb.AppendLine($"  - [{stick.Slot}] : {stick.CapacityBytes / 1024 / 1024 / 1024}GB @ {stick.SpeedMHz}MHz{vendor}");
        }
        sb.AppendLine($"  Total Installed: {m.Ram.TotalBytes / 1024 / 1024 / 1024}GB");

        sb.AppendLine("\nMOTHERBOARD");
        sb.AppendLine($"  {m.Board.Manufacturer} {m.Board.Product}");
        sb.AppendLine($"  [BIOS: {m.Board.BiosVersion}]");
        if (m.Board.TempC.HasValue)
            sb.AppendLine($"  Board temp: {m.Board.TempC.Value}C");

        sb.AppendLine("\nSTORAGE DRIVES");
        foreach (var d in m.Drives)
        {
            sb.AppendLine($"  {d.Model} - {d.SizeText} [{d.MediaType}]");
            foreach (var v in d.Volumes)
                sb.AppendLine($"    {v.Letter} {FormatSize(v.TotalBytes - v.FreeBytes)} used · {FormatSize(v.FreeBytes)} free");
        }

        sb.AppendLine("\nOPERATING SYSTEM");
        sb.AppendLine($"  {m.Os.Caption} ({m.Os.Architecture}) - Build {m.Os.BuildNumber}");
        sb.AppendLine($"  Activation: {(m.Os.IsActivated ? "Activated" : "Not activated")}");

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

    /// <summary>
    /// The full PCI key for a device ("10DE&2484&261719DA"): vendor,
    /// device and subsystem from the PNPDeviceID. Matches the key
    /// NvApiTemp builds from NvAPI_GPU_GetPCIIdentifiers, so a
    /// temperature lands on its exact card. Missing SUBSYS = null.
    /// </summary>
    private static string? GetPciKey(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        var pci = GetPciId(deviceId);
        if (pci == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(deviceId, @"SUBSYS_([0-9A-Fa-f]{8})");
        return m.Success ? pci + "&" + m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Bytes as "465 GB", or "5.5 TB" once past 1024 GB
    /// (one decimal — a 5589 GB volume reads better as "5.5 TB").
    /// Used for the per-volume used/free figures (0.15).</summary>
    public static string FormatSize(ulong bytes)
    {
        const double gib = 1024.0 * 1024 * 1024;
        double gb = bytes / gib;
        return gb >= 1024 ? $"{gb / 1024:0.#} TB" : $"{gb:0} GB";
    }

    private static string FormatVram(ulong bytes)
    {
        // Display VRAM as whole GB (8 GB, 12 GB). DXGI reports the
        // *available* dedicated memory, which is slightly under the
        // card's rated size (e.g. 7.83 GiB for an 8 GB card, because
        // the driver reserves some), so round to the nearest whole GB
        // instead of showing the raw MB figure. The exact byte count
        // still goes to the log for diagnosis.
        // Below 1 GB, show MB: an integrated GPU's 128 MB would
        // otherwise round to "0 GB".
        if (bytes < 1024UL * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024:0} MB";
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
