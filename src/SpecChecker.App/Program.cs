using Avalonia;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Elevated helper: install the bundled PawnIO driver and exit.
        // No window, no Avalonia — just the setup, then out. The main
        // app launches this copy with "runas" after the user agrees.
        if (args.Contains(PawnIoInstaller.InstallArg, StringComparer.OrdinalIgnoreCase))
        {
            var code = PawnIoInstaller.Install();
            try
            {
                AppPaths.EnsureAll();
                File.AppendAllText(
                    Path.Combine(AppPaths.LogsDir, "startup.log"),
                    $"[{DateTime.Now:HH:mm:ss}] --install-pawnio exited {code}{Environment.NewLine}");
            }
            catch { }
            Environment.Exit(code < 0 ? 1 : code);
            return;
        }

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
