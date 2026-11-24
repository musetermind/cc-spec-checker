using System.Management;

namespace SpecChecker.Core.Services;

/// <summary>
/// Drive health via SMART failure prediction (WMI root\WMI).
/// MSStorageDriver_FailurePredictStatus reports whether the drive's own
/// firmware predicts a failure. This needs admin rights to read in most
/// cases; when it cannot be read we return nothing and the report shows
/// "not available" instead of a fake health state. Never a fake number.
/// Full SMART attribute detail (temperature, power-on hours, reallocated
/// sectors) needs DeviceIoControl and admin too — that is the next step;
/// this is the health headline first.
/// </summary>
internal static class SmartHealth
{
    public record DriveHealth(string InstanceName, bool PredictFailure, uint Reason);

    public static List<DriveHealth> GetDriveHealth()
    {
        var list = new List<DriveHealth>();
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\WMI");
            scope.Connect();
            var query = new ObjectQuery("SELECT InstanceName, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["InstanceName"]?.ToString() ?? "?";
                var fail = mo["PredictFailure"] is bool b && b;
                var reason = mo["Reason"] is uint r ? r : 0;
                list.Add(new DriveHealth(name, fail, reason));
            }
        }
        catch (Exception ex)
        {
            // Admin-gated or unsupported: caller shows "not available".
            Logger.Write("SMART health read failed: " + ex.Message);
        }
        return list;
    }

    /// <summary>Short human label for a FailurePredictStatus Reason code.</summary>
    public static string ReasonLabel(uint reason) => reason switch
    {
        0 => "no failure predicted",
        1 => "failure predicted (threshold exceeded)",
        2 => "failure predicted (SMART trip)",
        _ => $"reason {reason}",
    };
}
