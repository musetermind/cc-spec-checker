using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
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

    /// <summary>One theme's colours. Dark is the green-on-black identity
    /// the app has always had; Light keeps the green, on white.</summary>
    private sealed record Palette(
        IBrush PageBg, IBrush HeaderBg, IBrush CardBg, IBrush Border,
        IBrush HeadlineFg, IBrush TitleFg, IBrush DetailFg, IBrush MutedFg,
        IBrush ButtonBg, IBrush ButtonFg, IBrush ButtonBorder, IBrush ButtonHoverBg,
        IBrush TempGreen, IBrush TempYellow, IBrush TempOrange, IBrush TempRed);

    private static readonly Palette DarkPalette = new(
        new SolidColorBrush(Color.Parse("#000000")), // PageBg
        new SolidColorBrush(Color.Parse("#0A0F0A")), // HeaderBg
        new SolidColorBrush(Color.Parse("#0D120D")), // CardBg
        new SolidColorBrush(Color.Parse("#1E521E")), // Border
        new SolidColorBrush(Color.Parse("#00FF00")), // HeadlineFg
        new SolidColorBrush(Color.Parse("#7FBF7F")), // TitleFg
        new SolidColorBrush(Color.Parse("#B8D8B8")), // DetailFg
        new SolidColorBrush(Color.Parse("#6A8A6A")), // MutedFg
        new SolidColorBrush(Color.Parse("#0D1F0D")), // ButtonBg
        new SolidColorBrush(Color.Parse("#00FF00")), // ButtonFg
        new SolidColorBrush(Color.Parse("#1E521E")), // ButtonBorder
        new SolidColorBrush(Color.Parse("#1A3A1A")), // ButtonHoverBg
        new SolidColorBrush(Color.Parse("#00FF00")), // TempGreen
        new SolidColorBrush(Color.Parse("#FFD700")), // TempYellow
        new SolidColorBrush(Color.Parse("#FF8C00")), // TempOrange
        new SolidColorBrush(Color.Parse("#FF3333"))); // TempRed

    private static readonly Palette LightPalette = new(
        new SolidColorBrush(Color.Parse("#F0F0F0")), // PageBg
        new SolidColorBrush(Color.Parse("#FFFFFF")), // HeaderBg
        new SolidColorBrush(Color.Parse("#FFFFFF")), // CardBg
        new SolidColorBrush(Color.Parse("#A5D6A7")), // Border
        new SolidColorBrush(Color.Parse("#1B5E20")), // HeadlineFg
        new SolidColorBrush(Color.Parse("#2E7D32")), // TitleFg
        new SolidColorBrush(Color.Parse("#333333")), // DetailFg
        new SolidColorBrush(Color.Parse("#757575")), // MutedFg
        new SolidColorBrush(Color.Parse("#E8F5E9")), // ButtonBg
        new SolidColorBrush(Color.Parse("#1B5E20")), // ButtonFg
        new SolidColorBrush(Color.Parse("#A5D6A7")), // ButtonBorder
        new SolidColorBrush(Color.Parse("#C8E6C9")), // ButtonHoverBg
        new SolidColorBrush(Color.Parse("#2E7D32")), // TempGreen
        new SolidColorBrush(Color.Parse("#B8860B")), // TempYellow
        new SolidColorBrush(Color.Parse("#E65100")), // TempOrange
        new SolidColorBrush(Color.Parse("#C62828"))); // TempRed

    private Palette _palette = DarkPalette;
    private string _themeMode = "Dark";

    /// <summary>Every button whose colours follow the palette (header,
    /// theme and tool-strip buttons). ApplyPalette repaints them all.</summary>
    private readonly List<Button> _allButtons = new();

    /// <summary>The brush for a temperature: green ≤70, yellow ≤80, orange ≤97, red above.</summary>
    private IBrush TempBrush(int tempC) => tempC switch
    {
        <= 70 => _palette.TempGreen,
        <= 80 => _palette.TempYellow,
        <= 97 => _palette.TempOrange,
        _ => _palette.TempRed,
    };

    public MainWindow()
    {
        InitializeComponent();
        // The app icon (Sithu, 2.0): the CPU-on-motherboard icon, loaded
        // from the embedded PNG for the window's title bar. Cosmetic
        // only — if it ever fails to load, the app runs on without a
        // title-bar icon rather than crashing (the exe icon, set by
        // ApplicationIcon in the .csproj, still applies).
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CCSpecChecker/CCSpecChecker-icon.png")));
        }
        catch
        {
            // No title-bar icon; continue normally.
        }
        // The shop branding lives in Branding.cs (one place for the
        // window title, header title, shop name, contact and links) so a
        // branded build for another shop is a values swap, not a rewrite.
        Title = Branding.WindowTitle;
        HeaderTitle.Text = Branding.HeaderTitle;
        // The shop name is a Facebook button only when the shop has a
        // page linked; otherwise it shows as plain text.
        if (Branding.FacebookUrl != null)
        {
            FacebookButtonText.Text = Branding.ShopName;
            ShopNameText.IsVisible = false;
        }
        else
        {
            FacebookButton.IsVisible = false;
            ShopNameText.Text = Branding.ShopName;
            ShopNameText.IsVisible = true;
        }
        CreditSuffix.Text = Branding.CreditSuffix;
        CreditContact.Text = Branding.ContactLine;
        // The second contact line (email): shown only when the brand
        // carries one (the CC build has none).
        CreditEmail.Text = Branding.EmailLine;
        CreditEmail.IsVisible = !string.IsNullOrEmpty(Branding.EmailLine);
        // The small centred credit at the very bottom: shown only when
        // the brand carries one (the CC build has none).
        CreditLine.Text = Branding.CreditLine;
        CreditLine.IsVisible = !string.IsNullOrEmpty(Branding.CreditLine);
        // The app runs unelevated by default (choice, not forced admin):
        // the core readings work without elevation. Offer the restart
        // button only when we are NOT already elevated — when already
        // admin it would be pointless, so hide it.
        AdminButton.IsVisible = !IsElevated();
        // The admin button gets a shield icon next to its label (Sithu,
        // 1.8); the icon's fill follows the button's foreground via
        // binding, so it repaints with the theme like the text does.
        AdminButton.Content = IconButtonContent(Icons.Admin, "Restart as administrator", 14);
        // Refresh / Copy / Save snapshot sit in the header now (2.1),
        // next to the admin button — same handlers as the strip had.
        RefreshButton.Content = IconButtonContent(Icons.Refresh, "Refresh", 12);
        CopyButton.Content = IconButtonContent(Icons.Copy, "Copy", 12);
        SaveSnapshotButton.Content = IconButtonContent(Icons.Save, "Save snapshot", 12);
        // Theme first (before the first LoadReport builds the cards, so
        // the dashboard renders in the right palette from the start).
        _themeMode = ThemeSettings.Load();
        if (Application.Current != null)
        {
            // In System mode the OS theme can change while the app runs;
            // follow it (ApplyPalette only — no save, no variant change,
            // so this cannot loop).
            Application.Current.ActualThemeVariantChanged += (_, _) =>
            {
                if (_themeMode == "System") ApplyPalette();
            };
        }
        StyleButton(AdminButton, 13, new Thickness(14, 7));
        StyleButton(RefreshButton, 11, new Thickness(8, 4));
        StyleButton(CopyButton, 11, new Thickness(8, 4));
        StyleButton(SaveSnapshotButton, 11, new Thickness(8, 4));
        StyleButton(ThemeButton, 11, new Thickness(8, 4));
        BuildToolStrip();
        ApplyTheme(_themeMode);
        LoadReport();
    }

    /// <summary>True when this process is running with administrator rights.</summary>
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ----- Theme (Sithu, 0.15: Dark / Light / System) -----

    /// <summary>Switch the theme: set the Fluent variant (so scrollbars,
    /// popups and the window chrome follow), repaint the palette, and
    /// remember the choice for the next launch.</summary>
    private void ApplyTheme(string mode)
    {
        _themeMode = mode;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = mode switch
            {
                "Light" => ThemeVariant.Light,
                "System" => ThemeVariant.Default, // follow the OS
                _ => ThemeVariant.Dark,
            };
        }
        ApplyPalette();
        ThemeSettings.Save(mode);
    }

    /// <summary>Pick the palette for the current mode (System follows
    /// the OS theme) and repaint the window, header, strip, status bar
    /// and every button, then rebuild the dashboard in the new colours.</summary>
    private void ApplyPalette()
    {
        _palette = _themeMode == "Light" ? LightPalette
            : _themeMode == "System" && Application.Current?.ActualThemeVariant == ThemeVariant.Light ? LightPalette
            : DarkPalette;
        Background = _palette.PageBg;
        Foreground = _palette.HeadlineFg;
        HeaderBorder.Background = _palette.HeaderBg;
        HeaderBorder.BorderBrush = _palette.Border;
        ToolStripBorder.Background = _palette.HeaderBg;
        ToolStripBorder.BorderBrush = _palette.Border;
        StatusBorder.Background = _palette.HeaderBg;
        StatusBorder.BorderBrush = _palette.Border;
        HeaderTitle.Foreground = _palette.HeadlineFg;
        CreditPrefix.Foreground = _palette.HeadlineFg;
        CreditSuffix.Foreground = _palette.HeadlineFg;
        CreditContact.Foreground = _palette.HeadlineFg;
        CreditEmail.Foreground = _palette.HeadlineFg;
        FacebookButton.Foreground = _palette.HeadlineFg;
        ShopNameText.Foreground = _palette.HeadlineFg;
        StatusText.Foreground = _palette.HeadlineFg;
        ReadOnlyNote.Foreground = _palette.HeadlineFg;
        CreditLine.Foreground = _palette.HeadlineFg;
        foreach (var b in _allButtons) UpdateButtonColors(b);
        ThemeButton.Content = $"Theme: {_themeMode}";
        if (_model != null) ContentArea.Content = BuildSummary(_model);
    }

    /// <summary>An icon for a card title: a vector path in the title
    /// colour. Cards are rebuilt on every theme change (ApplyPalette
    /// calls BuildSummary), so the fill can be set at build time.</summary>
    private Avalonia.Controls.Shapes.Path CardIcon(Geometry icon)
    {
        return new Avalonia.Controls.Shapes.Path
        {
            Data = icon,
            Fill = _palette.TitleFg,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>An icon for a button label: a vector path whose fill is
    /// bound to the button's foreground, so it follows the theme exactly
    /// like the text. Buttons are built once, not rebuilt on theme
    /// change, so a fixed fill would go stale.</summary>
    private static Avalonia.Controls.Shapes.Path ButtonIcon(Geometry icon, double size)
    {
        var p = new Avalonia.Controls.Shapes.Path
        {
            Data = icon,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        p.Bind(Shape.FillProperty, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
            {
                AncestorType = typeof(Button),
                AncestorLevel = 1,
            },
        });
        return p;
    }

    /// <summary>A button's content: icon + label in a row, centred as a
    /// group by the button.</summary>
    private static StackPanel IconButtonContent(Geometry icon, string label, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(ButtonIcon(icon, size));
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>One-time button setup: size, shape, hand cursor, hover
    /// handlers, and registration for palette repaints.</summary>
    private void StyleButton(Button b, double fontSize, Thickness padding)
    {
        b.FontSize = fontSize;
        b.Padding = padding;
        b.CornerRadius = new CornerRadius(4);
        b.Cursor = new Cursor(StandardCursorType.Hand);
        b.PointerEntered += OnButtonPointerEntered;
        b.PointerExited += OnButtonPointerExited;
        _allButtons.Add(b);
        UpdateButtonColors(b);
    }

    private void UpdateButtonColors(Button b)
    {
        b.Background = _palette.ButtonBg;
        b.Foreground = _palette.ButtonFg;
        b.BorderBrush = _palette.ButtonBorder;
        b.BorderThickness = new Thickness(1);
    }

    private void OnButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button b) b.Background = _palette.ButtonHoverBg;
    }

    private void OnButtonPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button b) b.Background = _palette.ButtonBg;
    }

    /// <summary>The theme button cycles Dark -> Light -> System.</summary>
    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        ApplyTheme(_themeMode switch
        {
            "Dark" => "Light",
            "Light" => "System",
            _ => "Dark",
        });
    }

    /// <summary>The shop name in the header credit opens the shop's
    /// Facebook page in the default browser (Sithu, 0.15). No page
    /// linked means no button, so this never runs for such a brand.</summary>
    private void OnFacebook(object? sender, RoutedEventArgs e)
    {
        var url = Branding.FacebookUrl;
        if (url == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
            StatusText.Text = $"Opened {Branding.ShopName} on Facebook.";
            Logger.Write($"Opened {url}.");
        }
        catch (Exception ex)
        {
            Logger.Write("Could not open Facebook page: " + ex.Message);
            StatusText.Text = "Could not open the Facebook page — see log.";
        }
    }

    // ----- Quick-launch tool strip (Sithu, 0.15: two rows, six each) -----

    /// <summary>Build the tool strip: twelve Windows tools, two rows of
    /// six. Built in code (not XAML) so the buttons follow the active
    /// theme palette.</summary>
    private void BuildToolStrip()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        AddStripButton(grid, "Device Manager", "devmgmt", Icons.DeviceManager, 0, 0);
        AddStripButton(grid, "Disk Management", "diskmgmt", Icons.Storage, 1, 0);
        AddStripButton(grid, "Task Manager", "taskmgr", Icons.TaskManager, 2, 0);
        AddStripButton(grid, "Settings", "settings", Icons.Settings, 3, 0);
        AddStripButton(grid, "File Explorer", "explorer", Icons.FileExplorer, 4, 0);
        AddStripButton(grid, "Disk Cleanup", "cleanmgr", Icons.DiskCleanup, 5, 0);
        AddStripButton(grid, "Resource Monitor", "resmon", Icons.ResourceMonitor, 0, 1);
        AddStripButton(grid, "Reliability", "perfmon", Icons.ReliabilityMonitor, 1, 1);
        AddStripButton(grid, "Event Viewer", "eventviewer", Icons.EventViewer, 2, 1);
        AddStripButton(grid, "Command Prompt", "cmd", Icons.CommandPrompt, 3, 1);
        AddStripButton(grid, "Programs", "appwiz", Icons.Programs, 4, 1);
        AddStripButton(grid, "Services", "services", Icons.Services, 5, 1);
        ToolStripArea.Content = grid;
    }

    private void AddStripButton(Grid grid, string label, string tag, Geometry icon, int col, int row)
    {
        var b = new Button
        {
            Content = IconButtonContent(icon, label, 14),
            Tag = tag,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // 6px gaps between buttons, none at the strip's right/bottom edge.
            Margin = new Thickness(0, 0, col < 5 ? 6 : 0, row == 0 ? 6 : 0),
        };
        Grid.SetColumn(b, col);
        Grid.SetRow(b, row);
        b.Click += OnStripButtonClick;
        StyleButton(b, 11, new Thickness(8, 6));
        grid.Children.Add(b);
    }

    private void OnStripButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        switch (tag)
        {
            case "devmgmt": LaunchTool("Device Manager", "devmgmt.msc", null); break;
            case "diskmgmt": LaunchTool("Disk Management", "diskmgmt.msc", null); break;
            case "taskmgr": LaunchTool("Task Manager", "taskmgr.exe", null); break;
            case "settings": LaunchTool("Settings", "ms-settings:", null); break;
            case "explorer": LaunchTool("File Explorer", "explorer.exe", "shell:MyComputerFolder"); break;
            case "cleanmgr": LaunchTool("Disk Cleanup", "cleanmgr.exe", null); break;
            case "resmon": LaunchTool("Resource Monitor", "resmon.exe", null); break;
            case "perfmon": LaunchTool("Reliability Monitor", "perfmon.exe", "/rel"); break;
            case "eventviewer": LaunchTool("Event Viewer", "eventvwr.msc", null); break;
            case "cmd": LaunchTool("Command Prompt", "cmd.exe", null); break;
            case "appwiz": LaunchTool("Programs and Features", "appwiz.cpl", null); break;
            case "services": LaunchTool("Services", "services.msc", null); break;
        }
    }

    /// <summary>Open a Windows tool via the shell (UseShellExecute).
    /// A failed launch never crashes the app — it lands in the status
    /// bar and the log instead.</summary>
    private void LaunchTool(string label, string file, string? args)
    {
        try
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(args)) start.Arguments = args;
            Process.Start(start);
            StatusText.Text = $"Opened {label}.";
            Logger.Write($"Opened {label} ({file}{(args != null ? " " + args : "")}).");
        }
        catch (Exception ex)
        {
            Logger.Write($"Could not open {label}: " + ex.Message);
            StatusText.Text = $"Could not open {label} — see log.";
        }
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
                Foreground = _palette.HeadlineFg,
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
        // ("Restart as Admin to check CPU temperature").
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
                : "Restart as Admin to check CPU temperature", null));
        }
        cards.Add(MakeCard(Icons.Cpu, "PROCESSOR", Shorten(m.Cpu.Name), cpuLines));

        // Graphics card — the VRAM is the star, so it is the headline.
        var gpu = m.Gpus.FirstOrDefault();
        if (gpu != null)
        {
            var vram = gpu.VramText ?? "VRAM unknown";
            // Board-partner brand next to the model (Sithu, 0.16):
            // "NVIDIA GeForce RTX 3070 · Zotac". No brand when unknown.
            var gpuLines = new List<(string Text, IBrush? Brush)>
            {
                (gpu.Name + (gpu.Brand != null ? $" · {gpu.Brand}" : ""), null),
            };
            if (gpu.TempC.HasValue)
            {
                var temp = $"{gpu.TempC.Value}C"
                    + (gpu.HotspotTempC.HasValue ? $" · hotspot {gpu.HotspotTempC.Value}C" : "");
                gpuLines.Add((temp, TempBrush(gpu.TempC.Value)));
            }
            cards.Add(MakeCard(Icons.Graphics, "GRAPHICS CARD", vram + " VRAM", gpuLines));
        }
        else
        {
            cards.Add(MakeCard(Icons.Graphics, "GRAPHICS CARD", "No GPU found", new List<(string Text, IBrush? Brush)>()));
        }

        // Memory card — summary line, then one line per stick.
        var ramLines = new List<(string Text, IBrush? Brush)>();
        var totalGb = m.Ram.TotalBytes / 1024 / 1024 / 1024;
        var speed = m.Ram.Sticks.FirstOrDefault()?.SpeedMHz ?? 0;
        var ramSummary = m.Ram.Sticks.Count > 0
            ? $"{m.Ram.Sticks.Count} stick{(m.Ram.Sticks.Count == 1 ? "" : "s")}"
              + (!string.IsNullOrEmpty(m.Ram.MemoryType) ? $" · {m.Ram.MemoryType}" : "")
              + (speed > 0 ? $" @ {speed}MHz" : "")
              + (!string.IsNullOrEmpty(m.Ram.ChannelMode) ? $" · {m.Ram.ChannelMode}" : "")
            : "No RAM sticks found";
        ramLines.Add((ramSummary, null));
        foreach (var s in m.Ram.Sticks)
        {
            var vendor = string.IsNullOrWhiteSpace(s.Manufacturer) ? "" : $" · {s.Manufacturer}";
            ramLines.Add(($"{s.CapacityBytes / 1024 / 1024 / 1024} GB @ {s.SpeedMHz}MHz{vendor}", null));
        }
        if (m.Ram.TempC.HasValue)
            ramLines.Add(($"{m.Ram.TempC.Value}C", TempBrush(m.Ram.TempC.Value)));
        cards.Add(MakeCard(Icons.Memory, "MEMORY", $"{totalGb} GB", ramLines));

        // Motherboard card
        var boardHead = string.IsNullOrWhiteSpace(m.Board.Product) ? "Unknown board" : m.Board.Product;
        var boardLines = new List<(string Text, IBrush? Brush)>
        {
            (m.Board.Manufacturer
                + (string.IsNullOrWhiteSpace(m.Board.BiosVersion) ? "" : $" · BIOS {m.Board.BiosVersion}"), null),
        };
        if (m.Board.TempC.HasValue)
            boardLines.Add(($"{m.Board.TempC.Value}C", TempBrush(m.Board.TempC.Value)));
        cards.Add(MakeCard(Icons.Board, "MOTHERBOARD", boardHead, boardLines));

        // Storage card — one line per drive (model · size · type), plus
        // one muted line per volume with its letter, used and free space
        // (Sithu, 0.15). No drive temp: the LHM storage sensor is
        // unreliable (94C for a drive HD Sentinel reads at 39C), so none
        // is shown.
        var storageLines = new List<(string Text, IBrush? Brush)>();
        foreach (var d in m.Drives)
        {
            storageLines.Add(($"{d.Model} · {d.SizeText} · {d.MediaType}", null));
            foreach (var v in d.Volumes)
                storageLines.Add(($"  {v.Letter} {SpecService.FormatSize(v.TotalBytes - v.FreeBytes)} used · {SpecService.FormatSize(v.FreeBytes)} free", _palette.MutedFg));
        }
        var storageHead = m.Drives.Count > 0
            ? $"{m.Drives.Count} drive{(m.Drives.Count == 1 ? "" : "s")}"
            : "No drives found";
        cards.Add(MakeCard(Icons.Storage, "STORAGE", storageHead, storageLines));

        // Operating system card
        var osHead = string.IsNullOrWhiteSpace(m.Os.Caption) ? "Unknown OS" : ShortOs(m.Os.Caption);
        var osDetail = $"{m.Os.Architecture} · Build {m.Os.BuildNumber} · {(m.Os.IsActivated ? "Activated" : "Not activated")}";
        cards.Add(MakeCard(Icons.Os, "OPERATING SYSTEM", osHead, new List<(string Text, IBrush? Brush)> { (osDetail, null) }));

        for (var i = 0; i < cards.Count; i++)
        {
            Grid.SetColumn(cards[i], i % 2);
            Grid.SetRow(cards[i], i / 2);
            grid.Children.Add(cards[i]);
        }

        return grid;
    }

    /// <summary>
    /// One dashboard card: a small muted title with an icon, a large
    /// bright headline, and a few short detail lines — one fact per line,
    /// so a glance is enough (Sithu, 0.13). A line takes the given brush
    /// when one is passed (a temperature uses its colour band); otherwise
    /// the soft green. Cards are display only: no clicks, no sections to
    /// go to. The icon is a vector path in the title colour (Sithu, 1.8).
    /// </summary>
    private Border MakeCard(Geometry? icon, string title, string headline, List<(string Text, IBrush? Brush)> lines)
    {
        var stack = new StackPanel { Spacing = 6 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon != null) titleRow.Children.Add(CardIcon(icon));
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = _palette.TitleFg,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(titleRow);
        stack.Children.Add(new TextBlock
        {
            Text = headline,
            Foreground = _palette.HeadlineFg,
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
                Foreground = brush ?? _palette.DetailFg,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return new Border
        {
            Background = _palette.CardBg,
            BorderBrush = _palette.Border,
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
            var path = System.IO.Path.Combine(dir, $"specchecker-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
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
