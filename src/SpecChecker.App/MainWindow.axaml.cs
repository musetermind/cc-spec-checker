using System.Diagnostics;
using System.Security.Principal;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SpecChecker.Core.Services;

namespace SpecChecker.App;

public partial class MainWindow : Window
{
    private string _fullReport = "";

    // Report section headings (as produced by SpecService.GetReport).
    private static readonly Dictionary<string, string> CategoryHeadings = new()
    {
        ["Operating System"] = "OPERATING SYSTEM",
        ["CPU"] = "PROCESSOR",
        ["RAM"] = "MEMORY (RAM)",
        ["Motherboard"] = "MOTHERBOARD",
        ["Graphics"] = "GRAPHICS CARD",
        ["Storage"] = "STORAGE DRIVES",
        ["Drive Health"] = "DRIVE HEALTH (SMART)",
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

    private void LoadReport()
    {
        try
        {
            StatusText.Text = "Reading hardware...";
            _fullReport = SpecService.GetReport();
            ShowCategory("Summary");
            StatusText.Text = $"Ready. Read at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Logger.Write("LoadReport failed: " + ex.Message);
            _fullReport = "Could not read this machine's specs: " + ex.Message;
            ReportBox.Text = _fullReport;
            StatusText.Text = "Read failed — see log.";
        }
    }

    private void ShowCategory(string category)
    {
        if (category == "Summary" || !CategoryHeadings.TryGetValue(category, out var heading))
        {
            ReportBox.Text = _fullReport;
            return;
        }

        // Extract the section starting at its heading, up to the next heading
        // (a line of "=" or the next all-caps section title).
        var lines = _fullReport.Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim() == heading);
        if (start < 0)
        {
            ReportBox.Text = $"{heading}: not available on this machine.";
            return;
        }
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length > 0 && t.All(c => c == '='))
            {
                end = i;
                break;
            }
            if (CategoryHeadings.Values.Any(h => h != heading && t == h))
            {
                end = i;
                break;
            }
        }
        ReportBox.Text = string.Join("\n", lines.Skip(start).Take(end - start)).Trim() + "\n";
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is ListBoxItem item && item.Content is string category)
            ShowCategory(category);
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => LoadReport();

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = ReportBox.Text ?? "";
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
}
