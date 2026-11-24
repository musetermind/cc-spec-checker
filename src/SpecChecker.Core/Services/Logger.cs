namespace SpecChecker.Core.Services;

/// <summary>Thread-safe timestamped log. UI subscribes to <see cref="LineWritten"/>.</summary>
public static class Logger
{
    private static readonly object _gate = new();
    private static readonly string _file = Path.Combine(AppPaths.LogsDir,
        $"specchecker-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    private static readonly List<string> _history = new();
    private const int MaxHistory = 300;

    public static event Action<string>? LineWritten;

    /// <summary>Recent lines, so a UI that subscribes late can catch up.</summary>
    public static IReadOnlyList<string> History
    {
        get { lock (_gate) return _history.ToList(); }
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _history.Add(line);
            if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
            try
            {
                AppPaths.EnsureAll();
                File.AppendAllText(_file, line + Environment.NewLine);
            }
            catch { /* logging must never crash the tool */ }
        }
        LineWritten?.Invoke(line);
    }

    public static void Section(string title)
    {
        Write(new string('=', 60));
        Write(title);
        Write(new string('=', 60));
    }
}
