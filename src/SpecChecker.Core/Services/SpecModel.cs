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

/// <summary>Processor: name, package temp, hottest core temp (null when unreadable).</summary>
public sealed record CpuInfo(string Name, int? PackageTempC, int? HottestCoreTempC);

/// <summary>Graphics card: name, VRAM as a display string ("8 GB", null when unknown), temp in C (null when unreadable).</summary>
public sealed record GpuInfo(string Name, string? VramText, int? TempC);

/// <summary>One RAM stick: the slot label WMI reports, capacity in bytes, speed in MHz.</summary>
public sealed record RamStick(string Slot, ulong CapacityBytes, uint SpeedMHz);

/// <summary>Installed RAM: the sticks and their total.</summary>
public sealed record RamInfo(List<RamStick> Sticks, ulong TotalBytes);

/// <summary>Motherboard: manufacturer, product name, BIOS version string.</summary>
public sealed record BoardInfo(string Manufacturer, string Product, string BiosVersion);

/// <summary>One storage drive: model, size as a display string ("465 GB"), media type.</summary>
public sealed record DriveInfo(string Model, string SizeText, string MediaType);

/// <summary>Operating system: caption, architecture, build number.</summary>
public sealed record OsInfo(string Caption, string Architecture, string BuildNumber);
