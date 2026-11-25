using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

/// <summary>
/// Modern dashboard UI. The engine hands us a structured SpecModel; this
/// window renders it as cards (Summary) and label/value detail panels.
/// The plain-text report still exists for Copy and Save snapshot.
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
    // hotspot), board, RAM and drive temps.
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
            ShowCategory("Summary");
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

    // ----- Navigation -----

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is ListBoxItem item && item.Content is string category)
            ShowCategory(category);
    }

    private void ShowCategory(string category)
    {
        if (_model == null) return;
        ContentArea.Content = category switch
        {
            "Summary" => BuildSummary(_model),
            "Operating System" => BuildDetail("Operating System", OsRows(_model)),
            "CPU" => BuildDetail("Processor", CpuRows(_model)),
            "RAM" => BuildDetail("Memory (RAM)", RamRows(_model)),
            "Motherboard" => BuildDetail("Motherboard", BoardRows(_model)),
            "Graphics" => BuildDetail("Graphics Card", GpuRows(_model)),
            "Storage" => BuildDetail("Storage Drives", DriveRows(_model)),
            "Drive Health" => BuildDetail("Drive Health (SMART)", HealthRows(_model)),
            _ => BuildSummary(_model),
        };
    }

    private void GoToCategory(string category)
    {
        foreach (var item in CategoryList.Items.OfType<ListBoxItem>())
        {
            if (item.Content is string c && c == category)
            {
                CategoryList.SelectedItem = item;
                break;
            }
        }
    }

    // ----- Dashboard (Summary) -----

    private Control BuildSummary(SpecModel m)
    {
        // Two-column dashboard grid (UniformGrid is not available in this
        // Avalonia build, so a plain Grid with explicit placement is used).
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };

        var cards = new List<Control>();

        // Processor card
        var cpuTemp = m.Cpu.PackageTempC.HasValue
            ? $"{m.Cpu.PackageTempC.Value}C package"
            : SpecService.IsElevated()
                ? "temp not available (PawnIO driver not installed)"
                : "restart as admin to check cpu temp";
        if (m.Cpu.HottestCoreTempC.HasValue && m.Cpu.PackageTempC.HasValue
            && m.Cpu.HottestCoreTempC.Value != m.Cpu.PackageTempC.Value)
            cpuTemp += $" · hottest core {m.Cpu.HottestCoreTempC.Value}C";
        var cpuDetailBrush = m.Cpu.PackageTempC.HasValue
            ? TempBrush(m.Cpu.PackageTempC.Value)
            : (IBrush?)null;
        cards.Add(MakeCard("PROCESSOR", Shorten(m.Cpu.Name), cpuTemp, () => GoToCategory("CPU"), cpuDetailBrush));

        // Graphics card — the VRAM is the star, so it is the headline.
        var gpu = m.Gpus.FirstOrDefault();
        if (gpu != null)
        {
            var vram = gpu.VramText ?? "VRAM unknown";
            var gDetail = gpu.Name
                + (gpu.TempC.HasValue ? $" · {gpu.TempC.Value}C" : "")
                + (gpu.HotspotTempC.HasValue ? $" · hotspot {gpu.HotspotTempC.Value}C" : "");
            var gpuDetailBrush = gpu.TempC.HasValue ? TempBrush(gpu.TempC.Value) : (IBrush?)null;
            cards.Add(MakeCard("GRAPHICS CARD", vram + " VRAM", gDetail, () => GoToCategory("Graphics"), gpuDetailBrush));
        }
        else
        {
            cards.Add(MakeCard("GRAPHICS CARD", "No GPU found", "", () => GoToCategory("Graphics")));
        }

        // Memory card
        var totalGb = m.Ram.TotalBytes / 1024 / 1024 / 1024;
        var speed = m.Ram.Sticks.FirstOrDefault()?.SpeedMHz ?? 0;
        var ramDetail = m.Ram.Sticks.Count > 0
            ? $"{m.Ram.Sticks.Count} stick{(m.Ram.Sticks.Count == 1 ? "" : "s")}"
              + (!string.IsNullOrEmpty(m.Ram.MemoryType) ? $" · {m.Ram.MemoryType}" : "")
              + (speed > 0 ? $" @ {speed}MHz" : "")
              + (m.Ram.TempC.HasValue ? $" · {m.Ram.TempC.Value}C" : "")
            : "No RAM sticks found";
        cards.Add(MakeCard("MEMORY", $"{totalGb} GB", ramDetail, () => GoToCategory("RAM")));

        // Motherboard card
        var boardHead = string.IsNullOrWhiteSpace(m.Board.Product) ? "Unknown board" : m.Board.Product;
        var boardDetail = m.Board.Manufacturer
            + (string.IsNullOrWhiteSpace(m.Board.BiosVersion) ? "" : $" · BIOS {m.Board.BiosVersion}")
            + (m.Board.TempC.HasValue ? $" · {m.Board.TempC.Value}C" : "");
        cards.Add(MakeCard("MOTHERBOARD", boardHead, boardDetail, () => GoToCategory("Motherboard")));

        // Storage card
        var first = m.Drives.FirstOrDefault();
        var storageHead = first != null ? first.Model : "No drives found";
        var storageDetail = first != null
            ? $"{first.SizeText} · {first.MediaType}"
              + (first.TempC.HasValue ? $" · {first.TempC.Value}C" : "")
              + (m.Drives.Count > 1 ? $" · {m.Drives.Count} drives" : "")
            : "";
        cards.Add(MakeCard("STORAGE", storageHead, storageDetail, () => GoToCategory("Storage")));

        // Operating system card
        var osHead = string.IsNullOrWhiteSpace(m.Os.Caption) ? "Unknown OS" : ShortOs(m.Os.Caption);
        var osDetail = $"{m.Os.Architecture} · Build {m.Os.BuildNumber} · {(m.Os.IsActivated ? "Activated" : "Not activated")}";
        cards.Add(MakeCard("OPERATING SYSTEM", osHead, osDetail, () => GoToCategory("Operating System")));

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
    /// and a smaller detail line. Clicking it opens that category.
    /// The detail line takes the given brush when one is passed (a
    /// temperature uses its colour band); otherwise the soft green.
    /// </summary>
    private Border MakeCard(string title, string headline, string detail, Action onClick, IBrush? detailBrush = null)
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
        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = detailBrush ?? DetailFg,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var border = new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 12, 12),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
        };
        border.PointerPressed += (_, _) => onClick();
        return border;
    }

    // ----- Detail panels -----

    /// <summary>A detail panel: a section title plus label/value rows.</summary>
    private Control BuildDetail(string title, List<(string Label, string Value, IBrush? ValueBrush)> rows)
    {
        var stack = new StackPanel { Spacing = 0 };

        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = TitleFg,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 4, 0, 12),
        });

        foreach (var (label, value, brush) in rows)
            stack.Children.Add(MakeRow(label, value, brush));

        if (rows.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Nothing to show here.",
                Foreground = MutedFg,
                FontSize = 13,
            });
        }

        return stack;
    }

    /// <summary>One label/value row: label on the left (muted), value on the right
    /// (bright green by default, or the given brush — temperatures use their
    /// colour band), with a divider.</summary>
    private Border MakeRow(string label, string value, IBrush? valueBrush = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*"),
            Margin = new Thickness(0, 0, 0, 0),
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = MutedFg,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 9, 12, 9),
        });
        Grid.SetColumn(grid.Children[0], 0);
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = valueBrush ?? HeadlineFg,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 9),
        };
        grid.Children.Add(valueBlock);
        Grid.SetColumn(valueBlock, 1);

        return new Border
        {
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    // ----- Rows per category -----

    private static List<(string, string, IBrush?)> CpuRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>
        {
            ("Model", m.Cpu.Name, null),
        };
        if (m.Cpu.Cores > 0)
            rows.Add(("Cores / Threads", $"{m.Cpu.Cores} cores · {m.Cpu.Threads} threads", null));
        if (m.Cpu.L3CacheKB > 0 || m.Cpu.L2CacheKB > 0)
            rows.Add(("Cache", $"L2 {m.Cpu.L2CacheKB / 1024}MB · L3 {m.Cpu.L3CacheKB / 1024}MB", null));
        if (m.Cpu.MaxClockMHz > 0)
            rows.Add(("Max clock", $"{m.Cpu.MaxClockMHz / 1000.0:0.#} GHz", null));
        // CPU temp: when unelevated, point at the fix (restart as admin)
        // instead of naming the driver — that is what actually unlocks it
        // on this bench. When already elevated and still no temp, the
        // driver is the real gap, so name it then.
        if (m.Cpu.PackageTempC.HasValue)
            rows.Add(("Package temperature", $"{m.Cpu.PackageTempC.Value}C", TempBrush(m.Cpu.PackageTempC.Value)));
        else
            rows.Add(("Package temperature",
                SpecService.IsElevated()
                    ? "not available (PawnIO driver not installed)"
                    : "restart as admin to check cpu temp",
                null));
        if (m.Cpu.HottestCoreTempC.HasValue && m.Cpu.PackageTempC.HasValue
            && m.Cpu.HottestCoreTempC.Value != m.Cpu.PackageTempC.Value)
            rows.Add(("Hottest core", $"{m.Cpu.HottestCoreTempC.Value}C", TempBrush(m.Cpu.HottestCoreTempC.Value)));
        return rows;
    }

    private static List<(string, string, IBrush?)> GpuRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>();
        foreach (var g in m.Gpus)
        {
            rows.Add(("Model", g.Name, null));
            rows.Add(("VRAM", g.VramText != null ? $"{g.VramText} (via DXGI)" : "unknown", null));
            if (g.TempC.HasValue)
                rows.Add(("Temperature", $"{g.TempC.Value}C", TempBrush(g.TempC.Value)));
            else
                rows.Add(("Temperature", "not available", null));
            if (g.HotspotTempC.HasValue)
                rows.Add(("Hotspot", $"{g.HotspotTempC.Value}C", TempBrush(g.HotspotTempC.Value)));
            rows.Add(("", "", null));
        }
        if (rows.Count > 0) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    private static List<(string, string, IBrush?)> RamRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>
        {
            ("Total installed", $"{m.Ram.TotalBytes / 1024 / 1024 / 1024} GB", null),
        };
        if (!string.IsNullOrEmpty(m.Ram.MemoryType))
            rows.Add(("Type", m.Ram.MemoryType, null));
        if (m.Ram.TempC.HasValue)
            rows.Add(("Module temperature", $"{m.Ram.TempC.Value}C", TempBrush(m.Ram.TempC.Value)));
        foreach (var s in m.Ram.Sticks)
        {
            var vendor = string.IsNullOrWhiteSpace(s.Manufacturer) ? "" : $" · {s.Manufacturer}";
            rows.Add(($"Slot {s.Slot}", $"{s.CapacityBytes / 1024 / 1024 / 1024} GB @ {s.SpeedMHz}MHz{vendor}", null));
        }
        return rows;
    }

    private static List<(string, string, IBrush?)> BoardRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>
        {
            ("Manufacturer", m.Board.Manufacturer, null),
            ("Model", m.Board.Product, null),
            ("BIOS version", m.Board.BiosVersion, null),
        };
        if (m.Board.TempC.HasValue)
            rows.Add(("Board temperature", $"{m.Board.TempC.Value}C", TempBrush(m.Board.TempC.Value)));
        return rows;
    }

    private static List<(string, string, IBrush?)> DriveRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>();
        foreach (var d in m.Drives)
        {
            rows.Add(("Model", d.Model, null));
            rows.Add(("Size", d.SizeText, null));
            rows.Add(("Media type", d.MediaType, null));
            if (d.TempC.HasValue)
                rows.Add(("Temperature", $"{d.TempC.Value}C", TempBrush(d.TempC.Value)));
            rows.Add(("", "", null));
        }
        if (rows.Count > 0) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    private static List<(string, string, IBrush?)> HealthRows(SpecModel m)
    {
        var rows = new List<(string, string, IBrush?)>();
        if (m.DriveHealth.Drives.Count == 0)
        {
            var why = m.DriveHealth.Failure switch
            {
                SmartHealth.NoDataReason.AccessDenied => "needs administrator rights",
                SmartHealth.NoDataReason.NotExposed => "not exposed by these drives via WMI (NVMe/USB drives often do not publish it)",
                _ => "could not be read",
            };
            rows.Add(("SMART data", $"not available ({why})", null));
            return rows;
        }
        foreach (var h in m.DriveHealth.Drives)
        {
            rows.Add(("Drive", h.InstanceName, null));
            rows.Add(("Status", h.PredictFailure ? "FAILURE PREDICTED" : "OK", null));
            rows.Add(("Reason", SmartHealth.ReasonLabel(h.Reason), null));
            rows.Add(("", "", null));
        }
        if (rows.Count > 0) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    private static List<(string, string, IBrush?)> OsRows(SpecModel m)
    {
        return new List<(string, string, IBrush?)>
        {
            ("Edition", m.Os.Caption, null),
            ("Architecture", m.Os.Architecture, null),
            ("Build", m.Os.BuildNumber, null),
            ("Activation", m.Os.IsActivated ? "Activated" : "Not activated", null),
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
