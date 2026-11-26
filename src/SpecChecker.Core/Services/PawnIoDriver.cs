using Microsoft.Win32;

namespace SpecChecker.Core.Services;

/// <summary>
/// Detects the PawnIO driver the way LibreHardwareMonitor does: the
/// Uninstall registry key's DisplayVersion. The driver is installed
/// once per machine by its official setup; this class only reads,
/// never writes. PawnIO 2.2.0 is the bundled version (see
/// third-party/pawnio) — an older install is offered the same
/// install, and the setup upgrades it.
/// </summary>
public static class PawnIoDriver
{
    /// <summary>The bundled PawnIO version (see third-party/pawnio).</summary>
    public static readonly Version RequiredVersion = new(2, 2, 0);

    /// <summary>The installed PawnIO version, or null when the driver
    /// is not installed.</summary>
    public static Version? InstalledVersion
    {
        get
        {
            // Same key LibreHardwareMonitor reads (its PawnIo static
            // constructor): the 64-bit view first, then the 32-bit one.
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                    if (subKey?.GetValue("DisplayVersion") is string text
                        && Version.TryParse(text, out var version))
                        return version;
                }
                catch { /* A missing key or a denied read just means "not installed". */ }
            }
            return null;
        }
    }

    public static bool IsInstalled => InstalledVersion != null;

    public static bool IsUpToDate => InstalledVersion != null && InstalledVersion >= RequiredVersion;
}
