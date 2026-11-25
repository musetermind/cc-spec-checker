using SpecChecker.Core.Services;

namespace SpecChecker.App;

/// <summary>
/// The saved Dark / Light / System theme choice (Sithu, 0.15). One line
/// in CCSpecChecker\settings.txt beside the exe — portable, in the same
/// folder as Logs/ and Snapshots/. Missing or invalid = Dark, the look
/// the app has always had.
/// </summary>
public static class ThemeSettings
{
    private static string Path => System.IO.Path.Combine(AppPaths.DataDir, "settings.txt");

    public static string Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var mode = File.ReadAllText(Path).Trim();
                if (mode is "Dark" or "Light" or "System") return mode;
            }
        }
        catch (Exception ex)
        {
            Logger.Write("Theme setting read failed: " + ex.Message);
        }
        return "Dark";
    }

    public static void Save(string mode)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(Path, mode);
        }
        catch (Exception ex)
        {
            Logger.Write("Theme setting save failed: " + ex.Message);
        }
    }
}
