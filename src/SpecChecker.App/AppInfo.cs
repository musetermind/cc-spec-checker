namespace SpecChecker.App;

/// <summary>
/// The app version, shown in the window title and the startup log line.
/// ONE place to bump on every release: 0.12, 0.13, 0.14... (Sithu, 0.12
/// onwards — the old "build N" numbering is retired). The .csproj
/// &lt;Version&gt; (assembly/file version) is kept in step with this
/// by hand; it cannot read this const.
/// </summary>
public static class AppInfo
{
    public const string Version = "0.12";
}
