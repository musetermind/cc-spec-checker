using System.Diagnostics;
using System.Security.Principal;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

/// <summary>
/// Installs the bundled PawnIO driver — the official 2.2.0 setup,
/// unmodified (see third-party/pawnio). The install runs in a separate
/// elevated copy of this exe (--install-pawnio), so the main window
/// never runs elevated for it: the user sees one prompt in the app,
/// then one UAC approval, and nothing is installed silently.
/// Exit codes come from the setup itself: 0 = installed, 183 = already
/// installed, 3010 = installed, reboot needed. Anything else failed.
/// </summary>
internal static class PawnIoInstaller
{
    /// <summary>The argument that turns this exe into the install helper.</summary>
    public const string InstallArg = "--install-pawnio";

    /// <summary>The embedded resource name of the bundled setup.</summary>
    public const string ResourceName = "PawnIO_setup.exe";

    /// <summary>Silent install, as the setup documents it.</summary>
    public const string SetupArgs = "-install -silent";

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsSuccess(int code) => code is 0 or 183 or 3010;

    public static bool NeedsReboot(int code) => code == 3010;

    /// <summary>
    /// The helper entry: extract the bundled setup to %TEMP%, run it
    /// silently, wait, delete the copy, return the setup's exit code.
    /// Only run elevated — installing a kernel driver needs admin, and
    /// the setup itself would fail without it.
    /// </summary>
    public static int Install()
    {
        if (!IsElevated())
        {
            Logger.Write("PawnIO install refused: helper is not elevated.");
            return -1;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
        try
        {
            using var stream = typeof(PawnIoInstaller).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Logger.Write("PawnIO install failed: embedded setup not found in the exe.");
                return -1;
            }
            using (var file = File.Create(tmp))
                stream.CopyTo(file);
            Logger.Write("PawnIO install: setup extracted to " + tmp);

            var psi = new ProcessStartInfo(tmp, SetupArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Logger.Write("PawnIO install failed: setup would not start.");
                return -1;
            }
            proc.WaitForExit();
            Logger.Write($"PawnIO install: setup exited {proc.ExitCode}.");
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Logger.Write("PawnIO install failed: " + ex.GetType().Name + ": " + ex.Message);
            return -1;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Launches this exe elevated to run the install. Returns the
    /// helper's exit code, or null when the user declined the UAC
    /// prompt or the launch failed.
    /// </summary>
    public static int? RequestInstall()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Logger.Write("PawnIO install request failed: could not find our own exe path.");
            return null;
        }
        try
        {
            var psi = new ProcessStartInfo(exe, InstallArg)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            // Win32Exception 1223 = the user declined the UAC prompt.
            Logger.Write("PawnIO install request failed: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }
}
