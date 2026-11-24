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
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsBatteryEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false,
            };
            _computer.Open();
            Logger.Write("LHM: Computer opened (CPU + board + GPU sensors).");
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
