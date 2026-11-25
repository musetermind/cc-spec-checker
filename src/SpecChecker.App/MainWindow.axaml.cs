using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

/// <summary>
/// Modern dashboard UI. The engine hands us a structured SpecModel; this
/// window renders it as a dashboard of cards — and ONLY cards (Sithu,
/// 0.13: no separate sections under the cards). Each card carries all
/// its info in a few short lines, so a glance is enough. The plain-text
/// report still exists for Copy and Save snapshot.
/// </summary>
public partial class MainWindow : Window
{
    private SpecModel? _model;
    private string _fullReport = "";

    // Colours for the green-on-black identity, refined: cards sit a touch
    // lighter than the page, borders are a muted green, headlines are the
    // bright green, labels are a soft green.
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#0D120D"));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#1E521E"));
    private static readonly IBrush HeadlineFg = new SolidColorBrush(Color.Parse("#00FF00"));
    private static readonly IBrush TitleFg = new SolidColorBrush(Color.Parse("#7FBF7F"));
    private static readonly IBrush DetailFg = new SolidColorBrush(Color.Parse("#B8D8B8"));
    private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#6A8A6A"));

    // Temperature colour scale (Sithu's bands): up to 70C green, up to 80C
    // yellow, up to 97C orange, above that red. Applies to CPU, GPU (and
    // hotspot), board and RAM temps.
    private static readonly IBrush TempGreen = new SolidColorBrush(Color.Parse("#00FF00"));
    private static readonly IBrush TempYellow = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush TempOrange = new SolidColorBrush(Color.Parse("#FF8C00"));
    private static readonly IBrush TempRed = new SolidColorBrush(Color.Parse("#FF3333"));

    /// <summary>The brush for a temperature: green ≤70, yellow ≤80, orange ≤97, red above.</summary>
    private static IBrush TempBrush(int tempC) => tempC switch
    {
        <= 70 => TempGreen,
        <= 80 => TempYellow,
        <= 97 => TempOrange,
        _ => TempRed,
    };

    public MainWindow()
    {
        InitializeComponent();
        // The version lives in one place (AppInfo.Version) so the title
        // can never drift from the release number again.
        Title = $"CC Spec Checker v{AppInfo.Version} — Computer Corner";
        // The header title carries the version too (Sithu, 0.14), with
        // the shop credit under it in the XAML.
        HeaderTitle.Text = $"CC SPEC CHECKER v{AppInfo.Version}";
        // The app runs unelevated by default (choice, not forced admin):
        // the core readings work without elevation. Offer the restart
        // button only when we are NOT already elevated — when already
        // admin it would be pointless, so hide it.
        AdminButton.IsVisible = !IsElevated();
        LoadReport();
    }

    /// <summary>True when this process is running with administrator rights.</summary>
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void LoadReport()
    {
        try
        {
            StatusText.Text = "Reading hardware...";
            _model = SpecService.GetModel();
            _fullReport = SpecService.GetReport();
            ContentArea.Content = BuildSummary(_model);
            StatusText.Text = $"Ready. Read at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Logger.Write("LoadReport failed: " + ex.Message);
            _fullReport = "Could not read this machine's specs: " + ex.Message;
            ContentArea.Content = new TextBlock
            {
                Text = _fullReport,
                Foreground = HeadlineFg,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            StatusText.Text = "Read failed — see log.";
        }
    }

    // ----- Dashboard (the whole UI) -----

    private Control BuildSummary(SpecModel m)
    {
        // Two-column dashboard grid (UniformGrid is not available in this
        // Avalonia build, so a plain Grid with explicit placement is used).
        // The cards ARE the UI: each one carries all its info in a few
        // short lines (Sithu, 0.13 — "a glance is enough"). No detail
        // sections below the cards, no clicks.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };

        var cards = new List<Control>();

        // Processor card — specs on the first line, the live temp on a
        // second line under it. The temp line keeps its colour band;
        // when there is no temp the line says what to do instead
        // ("restart as admin...").
        var cpuLines = new List<(string Text, IBrush? Brush)>();
        if (m.Cpu.Cores > 0)
        {
            var specs = $"{m.Cpu.Cores} cores · {m.Cpu.Threads} threads";
            var cache = SpecService.FormatCache(m.Cpu.L2CacheKB, m.Cpu.L3CacheKB);
            if (!string.IsNullOrEmpty(cache))
                specs += $" · {cache}";
            cpuLines.Add((specs, null));
        }
        if (m.Cpu.PackageTempC.HasValue)
        {
            var temp = $"{m.Cpu.PackageTempC.Value}C package";
            if (m.Cpu.HottestCoreTempC.HasValue
                && m.Cpu.HottestCoreTempC.Value != m.Cpu.PackageTempC.Value)
                temp += $" · hottest core {m.Cpu.HottestCoreTempC.Value}C";
            cpuLines.Add((temp, TempBrush(m.Cpu.PackageTempC.Value)));
        }
        else
        {
            cpuLines.Add((SpecService.IsElevated()
                ? "temp not available (PawnIO driver not installed)"
                : "restart as admin to check cpu temp", null));
        }
        cards.Add(MakeCard("PROCESSOR", Shorten(m.Cpu.Name), cpuLines));

        // Graphics card — the VRAM is the star, so it is the headline.
        var gpu = m.Gpus.FirstOrDefault();
        if (gpu != null)
        {
            var vram = gpu.VramText ?? "VRAM unknown";
            var gpuLines = new List<(string Text, IBrush? Brush)> { (gpu.Name, null) };
            if (gpu.TempC.HasValue)
            {
                var temp = $"{gpu.TempC.Value}C"
                    + (gpu.HotspotTempC.HasValue ? $" · hotspot {gpu.HotspotTempC.Value}C" : "");
                gpuLines.Add((temp, TempBrush(gpu.TempC.Value)));
            }
            cards.Add(MakeCard("GRAPHICS CARD", vram + " VRAM", gpuLines));
        }
        else
        {
            cards.Add(MakeCard("GRAPHICS CARD", "No GPU found", new List<(string Text, IBrush? Brush)>()));
        }

        // Memory card — summary line, then one line per stick.
        var ramLines = new List<(string Text, IBrush? Brush)>();
        var totalGb = m.Ram.TotalBytes / 1024 / 1024 / 1024;
        var speed = m.Ram.Sticks.FirstOrDefault()?.SpeedMHz ?? 0;
        var ramSummary = m.Ram.Sticks.Count > 0
            ? $"{m.Ram.Sticks.Count} stick{(m.Ram.Sticks.Count == 1 ? "" : "s")}"
              + (!string.IsNullOrEmpty(m.Ram.MemoryType) ? $" · {m.Ram.MemoryType}" : "")
              + (speed > 0 ? $" @ {speed}MHz" : "")
            : "No RAM sticks found";
        ramLines.Add((ramSummary, null));
        foreach (var s in m.Ram.Sticks)
        {
            var vendor = string.IsNullOrWhiteSpace(s.Manufacturer) ? "" : $" · {s.Manufacturer}";
            ramLines.Add(($"{s.CapacityBytes / 1024 / 1024 / 1024} GB @ {s.SpeedMHz}MHz{vendor}", null));
        }
        if (m.Ram.TempC.HasValue)
            ramLines.Add(($"{m.Ram.TempC.Value}C", TempBrush(m.Ram.TempC.Value)));
        cards.Add(MakeCard("MEMORY", $"{totalGb} GB", ramLines));

        // Motherboard card
        var boardHead = string.IsNullOrWhiteSpace(m.Board.Product) ? "Unknown board" : m.Board.Product;
        var boardLines = new List<(string Text, IBrush? Brush)>
        {
            (m.Board.Manufacturer
                + (string.IsNullOrWhiteSpace(m.Board.BiosVersion) ? "" : $" · BIOS {m.Board.BiosVersion}"), null),
        };
        if (m.Board.TempC.HasValue)
            boardLines.Add(($"{m.Board.TempC.Value}C", TempBrush(m.Board.TempC.Value)));
        cards.Add(MakeCard("MOTHERBOARD", boardHead, boardLines));

        // Storage card — one line per drive (model · size · type), so the
        // count in the headline is backed by the full list on the card.
        // No drive temp: the LHM storage sensor is unreliable (94C for a
        // drive HD Sentinel reads at 39C), so none is shown.
        var storageLines = new List<(string Text, IBrush? Brush)>();
        foreach (var d in m.Drives)
            storageLines.Add(($"{d.Model} · {d.SizeText} · {d.MediaType}", null));
        var storageHead = m.Drives.Count > 0
            ? $"{m.Drives.Count} drive{(m.Drives.Count == 1 ? "" : "s")}"
            : "No drives found";
        cards.Add(MakeCard("STORAGE", storageHead, storageLines));

        // Operating system card
        var osHead = string.IsNullOrWhiteSpace(m.Os.Caption) ? "Unknown OS" : ShortOs(m.Os.Caption);
        var osDetail = $"{m.Os.Architecture} · Build {m.Os.BuildNumber} · {(m.Os.IsActivated ? "Activated" : "Not activated")}";
        cards.Add(MakeCard("OPERATING SYSTEM", osHead, new List<(string Text, IBrush? Brush)> { (osDetail, null) }));

        for (var i = 0; i < cards.Count; i++)
        {
            Grid.SetColumn(cards[i], i % 2);
            Grid.SetRow(cards[i], i / 2);
            grid.Children.Add(cards[i]);
        }

        return grid;
    }

    /// <summary>
    /// One dashboard card: a small muted title, a large bright headline,
    /// and a few short detail lines — one fact per line, so a glance is
    /// enough (Sithu, 0.13). A line takes the given brush when one is
    /// passed (a temperature uses its colour band); otherwise the soft
    /// green. Cards are display only: no clicks, no sections to go to.
    /// </summary>
    private Border MakeCard(string title, string headline, List<(string Text, IBrush? Brush)> lines)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = TitleFg,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = headline,
            Foreground = HeadlineFg,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var (text, brush) in lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            stack.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = brush ?? DetailFg,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 12, 12),
            Child = stack,
        };
    }

    // ----- Small helpers -----

    /// <summary>Shorten a long CPU name for a card headline (keeps the model, drops the (R)/(TM) noise).</summary>
    private static string Shorten(string name)
    {
        return name
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ")
            .Trim();
    }

    /// <summary>"Microsoft Windows 11 Pro" -> "Windows 11 Pro".</summary>
    private static string ShortOs(string caption)
    {
        const string prefix = "Microsoft ";
        return caption.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? caption[prefix.Length..].Trim()
            : caption;
    }

    // ----- Header actions -----

    private void OnRefresh(object? sender, RoutedEventArgs e) => LoadReport();

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = _fullReport;
            if (text.Length > 0)
            {
                Clipboard?.SetTextAsync(text);
                StatusText.Text = "Report copied to clipboard.";
            }
        }
        catch (Exception ex)
        {
            Logger.Write("Copy failed: " + ex.Message);
            StatusText.Text = "Copy failed — see log.";
        }
    }

    private void OnSaveSnapshot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var dir = AppPaths.EnsureSnapshots();
            var path = Path.Combine(dir, $"specchecker-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, _fullReport);
            StatusText.Text = $"Snapshot saved: {path}";
            Logger.Write("Snapshot saved: " + path);
        }
        catch (Exception ex)
        {
            Logger.Write("Save snapshot failed: " + ex.Message);
            StatusText.Text = "Save failed — see log.";
        }
    }

    /// <summary>
    /// Relaunch this exe elevated (UAC prompt) and close the current
    /// unelevated instance. If the user cancels the UAC prompt, stay
    /// in this instance and say so — nothing is lost.
    /// </summary>
    private void OnRestartAsAdmin(object? sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                StatusText.Text = "Could not find this app's path — run it as administrator manually.";
                return;
            }
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas", // triggers the UAC prompt
            };
            Process.Start(start);
            // The elevated copy is starting; close this one.
            Close();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223 = ERROR_CANCELLED — the user said No to the UAC prompt.
            Logger.Write("Restart as administrator cancelled by the user.");
            StatusText.Text = "Administrator restart cancelled — still running unelevated.";
        }
        catch (Exception ex)
        {
            Logger.Write("Restart as administrator failed: " + ex.Message);
            StatusText.Text = "Could not restart as administrator — see log.";
        }
    }
}
