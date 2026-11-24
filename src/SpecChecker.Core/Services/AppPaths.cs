namespace SpecChecker.Core.Services;

/// <summary>
/// Portable layout: exactly ONE folder ever appears beside the exe — CCSpecChecker.
/// Inside it: Logs/ (the tool's own log files) and Snapshots/ (saved reports).
/// A customer desktop never gets littered. Move the folder, rename the drive
/// letter, run from USB — nothing breaks.
/// </summary>
public static class AppPaths
{
    public static string BaseDir { get; } = AppContext.BaseDirectory;
    public static string DataDir => Path.Combine(BaseDir, "CCSpecChecker");
    public static string LogsDir => Path.Combine(DataDir, "Logs");
    public static string SnapshotsDir => Path.Combine(DataDir, "Snapshots");

    /// <summary>
    /// Called lazily (first log write), never at startup. Creates CCSpecChecker/Logs.
    /// Snapshots/ is created on demand when a snapshot is actually saved.
    /// </summary>
    public static void EnsureAll()
    {
        Directory.CreateDirectory(LogsDir);
    }

    public static string EnsureSnapshots()
    {
        Directory.CreateDirectory(SnapshotsDir);
        return SnapshotsDir;
    }
}
