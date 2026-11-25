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
    SmartHealth.ReadResult DriveHealth,
    OsInfo Os,
    List<string> Monitors);

/// <summary>Processor: name, core/thread counts, cache sizes (KB), max clock
/// (MHz, the on-paper boost), package temp and hottest core temp (null when
/// unreadable).</summary>
public sealed record CpuInfo(
    string Name,
    int Cores,
    int Threads,
    uint L2CacheKB,
    uint L3CacheKB,
    uint MaxClockMHz,
    int? PackageTempC,
    int? HottestCoreTempC);

/// <summary>Graphics card: name, VRAM as a display string ("8 GB", null when
/// unknown), GPU temp and hotspot temp in °C (null when unreadable).</summary>
public sealed record GpuInfo(string Name, string? VramText, int? TempC, int? HotspotTempC);

/// <summary>One RAM stick: the slot label WMI reports, capacity in bytes,
/// speed in MHz, and the vendor (Manufacturer) when the stick reports one.</summary>
public sealed record RamStick(string Slot, ulong CapacityBytes, uint SpeedMHz, string Manufacturer);

/// <summary>Installed RAM: the sticks, their total, the memory type
/// ("DDR4"/"DDR5", from SMBIOS), and the module temp in °C (null when no
/// module reports one — normal for DDR4).</summary>
public sealed record RamInfo(List<RamStick> Sticks, ulong TotalBytes, string MemoryType, int? TempC);

/// <summary>Motherboard: manufacturer, product name, BIOS version string,
/// and the board temp in °C (null when no sensor reports one).</summary>
public sealed record BoardInfo(string Manufacturer, string Product, string BiosVersion, int? TempC);

/// <summary>One storage drive: model, size as a display string ("465 GB"),
/// media type, and the drive temp in °C (null when no sensor reports one).
/// No health percentage here — Sithu: SSD health does not matter.</summary>
public sealed record DriveInfo(string Model, string SizeText, string MediaType, int? TempC);

/// <summary>Operating system: caption, architecture, build number, and
/// whether Windows is activated (LicenseStatus 1 = Licensed).</summary>
public sealed record OsInfo(string Caption, string Architecture, string BuildNumber, bool IsActivated);
