using LibreHardwareMonitor.Hardware;

namespace SpecChecker.Core.Services;

/// <summary>
/// Real-time temperatures via LibreHardwareMonitor 0.9.6. CPU/board sensors
/// need the PawnIO driver (opt-in install); without it, CPU sensors simply
/// read nothing and the UI shows "not available". GPU temps come from NVAPI
/// (see NvApiTemp) and never need PawnIO. This service never installs
/// anything — it only reads what the driver already exposes.
/// </summary>
internal static class SensorService
{
    private static Computer? _computer;
    private static bool _openFailed;

    /// <summary>CPU package temp, hottest core temp, and board temp in °C
    /// (null when a value is not available). Safe to call repeatedly; the
    /// Computer is opened once and reused.</summary>
    public static (int? cpuPackage, int? hottestCore, int? board) GetCpuTemps()
    {
        var computer = EnsureOpen();
        if (computer == null) return (null, null, null);
        try
        {
            computer.Accept(new UpdateVisitor());
            int? package = null, hottest = null, board = null;
            foreach (var hw in computer.Hardware)
            {
                if (hw.HardwareType is not (HardwareType.Cpu or HardwareType.Motherboard))
                    continue;
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                        continue;
                    var name = s.Name ?? "";
                    var v = (int)Math.Round(s.Value.Value);
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            package ??= v;
                        else if (name.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
                            hottest = hottest == null ? v : Math.Max(hottest.Value, v);
                    }
                    else if (hw.HardwareType == HardwareType.Motherboard)
                    {
                        board ??= v;
                    }
                }
            }
            return (package, hottest, board);
        }
        catch (Exception ex)
        {
            Logger.Write("LHM sensor read failed: " + ex.GetType().Name + ": " + ex.Message);
            return (null, null, null);
        }
    }

    /// <summary>
    /// RAM module temperature in °C (null when no module reports one).
    /// DDR5 modules can carry a temp sensor; DDR4 almost never does, so
    /// a null here is normal, not a failure. Read from the Memory
    /// hardware (needs IsMemoryEnabled).
    /// </summary>
    public static int? GetRamTemp()
    {
        var computer = EnsureOpen();
        if (computer == null) return null;
        try
        {
            computer.Accept(new UpdateVisitor());
            foreach (var hw in computer.Hardware)
            {
                if (hw.HardwareType != HardwareType.Memory) continue;
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                        return (int)Math.Round(s.Value.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Write("LHM RAM temp read failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        return null;
    }

    // Drive temperatures are NOT read here. LHM's storage temperature
    // sensor proved unreliable on the bench: it reported 94C for the
    // ZHITAI Ti600 while HD Sentinel read the same drive at 39C
    // (Sithu, build 11). Showing a wrong temp is worse than showing
    // none, so the storage hardware stays disabled and no drive temp
    // is ever shown. If drive temps come back one day, they need a
    // verified source (SMART via DeviceIoControl), not this sensor.

    /// <summary>Close the LHM Computer and forget it, so the next read
    /// opens a fresh one. Used after the PawnIO driver is installed
    /// while the app is running: the old Computer was opened without
    /// the driver and would keep reporting no CPU sensors.</summary>
    public static void Reset()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
        _openFailed = false;
        Logger.Write("LHM: Computer closed for a fresh open (driver installed).");
    }

    private static Computer? EnsureOpen()
    {
        if (_computer != null) return _computer;
        if (_openFailed) return null;
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                // Storage sensors stay OFF: the LHM storage temperature
                // sensor is unreliable (it reported 94C for a drive HD
                // Sentinel reads at 39C), so no drive temps are read at
                // all rather than a wrong number (Sithu, build 11).
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsBatteryEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false,
            };
            _computer.Open();
            Logger.Write("LHM: Computer opened (CPU + board + GPU + memory sensors).");
            return _computer;
        }
        catch (Exception ex)
        {
            _openFailed = true;
            Logger.Write("LHM open failed (PawnIO/driver not present?): " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
