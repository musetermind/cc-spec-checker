using Avalonia;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // If the app dies before the window appears, leave the reason in a
        // text file inside CCSpecChecker/Logs — otherwise "it didn't run" is undebuggable.
        // Nothing is ever written beside the exe; the desktop stays clean.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                AppPaths.EnsureAll();
                File.WriteAllText(
                    Path.Combine(AppPaths.LogsDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"),
                    e.ExceptionObject?.ToString() ?? "unknown error");
            }
            catch { }
        };

        try
        {
            AppPaths.EnsureAll();
            File.AppendAllText(
                Path.Combine(AppPaths.LogsDir, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss}] starting, args=[{string.Join(' ', args)}]{Environment.NewLine}");
        }
        catch { }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
