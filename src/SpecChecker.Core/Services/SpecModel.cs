namespace SpecChecker.Core.Services;

/// <summary>
/// Structured system specs — the typed model behind both the dashboard UI
/// and the text report. One gathering pass, two renderings: the dashboard
/// cards read these records directly, and GetReport() renders them into
/// the plain-text report (same wording as before). Never a fake number:
/// a value that could not be read is null, and the UI/report say so.
/// </summary>
public sealed record SpecModel(
    CpuInfo Cpu,
    List<GpuInfo> Gpus,
    RamInfo Ram,
    BoardInfo Board,
    List<DriveInfo> Drives,
    OsInfo Os,
    List<string> Monitors);

/// <summary>Processor: name, core/thread counts, cache sizes (KB),
/// package temp and hottest core temp (null when unreadable). No max
/// clock — WMI's MaxClockSpeed is the base clock, not the boost, so
/// showing it as "max" was wrong (Sithu, build 11).</summary>
public sealed record CpuInfo(
    string Name,
    int Cores,
    int Threads,
    uint L2CacheKB,
    uint L3CacheKB,
    int? PackageTempC,
    int? HottestCoreTempC);

/// <summary>Graphics card: name, VRAM as a display string ("8 GB", null when
/// unknown), GPU temp and hotspot temp in °C (null when unreadable), and
/// the board-partner brand ("Zotac", "ASUS", null when unknown — read
/// from the PCI subsystem vendor ID, Sithu 0.16).</summary>
public sealed record GpuInfo(string Name, string? VramText, int? TempC, int? HotspotTempC, string? Brand);

/// <summary>One RAM stick: the slot label WMI reports, capacity in bytes,
/// speed in MHz, the vendor (Manufacturer) when the stick reports one,
/// and the CAS latency in clocks as WMI reports it — the negotiated
/// (running) CL, not the kit's rated CL; 0 = unknown (Sithu, 0.16).</summary>
public sealed record RamStick(string Slot, ulong CapacityBytes, uint SpeedMHz, string Manufacturer, uint CasLatency);

/// <summary>Installed RAM: the sticks, their total, the memory type
/// ("DDR4"/"DDR5", from SMBIOS), the module temp in °C (null when no
/// module reports one — normal for DDR4), and the channel mode
/// ("Dual Channel" / "Single Channel" / "Quad Channel", null when it
/// cannot be determined — Sithu, 0.16).</summary>
public sealed record RamInfo(List<RamStick> Sticks, ulong TotalBytes, string MemoryType, int? TempC, string? ChannelMode);

/// <summary>Motherboard: manufacturer, product name, BIOS version string,
/// and the board temp in °C (null when no sensor reports one).</summary>
public sealed record BoardInfo(string Manufacturer, string Product, string BiosVersion, int? TempC);

/// <summary>One volume (partition) on a drive: its letter ("C:"), total
/// size and free space in bytes (Sithu, 0.15 — drive letters, used and
/// free space on the Storage card).</summary>
public sealed record VolumeInfo(string Letter, ulong TotalBytes, ulong FreeBytes);

/// <summary>One storage drive: model, size as a display string ("465 GB"),
/// and the real drive type ("SSD (NVMe)", "SSD (SATA)", "HDD (SATA)",
/// "Removable (USB)") from MSFT_PhysicalDisk — WMI's MediaType says
/// "Fixed hard disk media" for SSDs and HDDs alike, so it is never shown
/// as-is when the Storage class answers (Sithu, build 12; the UInt16
/// cast bug that still showed it in build 12 fixed in 0.13). Volumes
/// lists the drive's letters with their used/free space (0.15); a drive
/// with no mounted volume has an empty list. No drive temp — LHM's
/// storage temperature sensor is unreliable (it reported 94C for a
/// drive HD Sentinel reads at 39C), so no temp is shown rather than a
/// wrong number. No health percentage here — Sithu: SSD health does
/// not matter.</summary>
public sealed record DriveInfo(string Model, string SizeText, string MediaType, List<VolumeInfo> Volumes);

/// <summary>Operating system: caption, architecture, build number, and
/// whether Windows is activated (LicenseStatus 1 = Licensed).</summary>
public sealed record OsInfo(string Caption, string Architecture, string BuildNumber, bool IsActivated);
