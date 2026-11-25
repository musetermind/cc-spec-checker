using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Theme before the window exists, so the Fluent chrome
            // (scrollbars, popups) starts in the saved variant. The
            // palette itself is applied by MainWindow.
            var mode = ThemeSettings.Load();
            Application.Current!.RequestedThemeVariant = mode switch
            {
                "Light" => ThemeVariant.Light,
                "System" => ThemeVariant.Default, // follow the OS
                _ => ThemeVariant.Dark,
            };
            Logger.Write($"{Branding.AppName} v{AppInfo.Version} initialized. System is ready.");
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
