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
/// Public because SpecModel (public) exposes its ReadResult.
/// </summary>
public static class SmartHealth
{
    public record DriveHealth(string InstanceName, bool PredictFailure, uint Reason);

    /// <summary>Why the SMART read produced no drives.</summary>
    public enum NoDataReason
    {
        /// <summary>The query worked but no drive exposed the WMI class
        /// (common for NVMe drives and USB sticks — they do not publish
        /// MSStorageDriver_FailurePredictStatus). Not an admin problem.</summary>
        NotExposed,
        /// <summary>The WMI read was denied — elevation is genuinely needed.</summary>
        AccessDenied,
        /// <summary>Some other failure (WMI unavailable, unexpected error).</summary>
        Other,
    }

    public record ReadResult(List<DriveHealth> Drives, NoDataReason? Failure);

    public static ReadResult GetDriveHealthDetailed()
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
            // Distinguish a real access problem from any other failure,
            // so the report never blames admin rights for a drive that
            // simply does not expose this WMI class.
            Logger.Write("SMART health read failed: " + ex.Message);
            var denied = ex is UnauthorizedAccessException
                || ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase);
            return new ReadResult(list, denied ? NoDataReason.AccessDenied : NoDataReason.Other);
        }
        return new ReadResult(list, list.Count == 0 ? NoDataReason.NotExposed : null);
    }

    public static List<DriveHealth> GetDriveHealth() => GetDriveHealthDetailed().Drives;

    /// <summary>Short human label for a FailurePredictStatus Reason code.</summary>
    public static string ReasonLabel(uint reason) => reason switch
    {
        0 => "no failure predicted",
        1 => "failure predicted (threshold exceeded)",
        2 => "failure predicted (SMART trip)",
        _ => $"reason {reason}",
    };
}
