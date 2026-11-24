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
    };

    public MainWindow()
    {
        InitializeComponent();
        LoadReport();
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
