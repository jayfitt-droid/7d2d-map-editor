using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Validate;
using Microsoft.Win32;

namespace MapEditor.App;

/// <summary>
/// Picks a world name and parent folder for an export, and shows the validation results first so problems are
/// seen before the world reaches the game. The world is written to Location\Name.
/// </summary>
public partial class ExportDialog : Window
{
    private readonly bool _hasErrors;

    public ExportDialog(string name, string location, IReadOnlyList<ValidationIssue> issues)
    {
        InitializeComponent();
        NameBox.Text = name;
        LocationBox.Text = location;
        IssuesList.ItemsSource = issues;

        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        _hasErrors = errors > 0;
        IssuesHeader.Text = issues.Count == 0
            ? "✓ No problems found."
            : $"{Plural(errors, "error")}, {Plural(warnings, "warning")}. Double-click an issue in the Issues panel to find it on the map.";
        IssuesList.Visibility = issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_hasErrors) ExportButton.Content = "Export anyway";

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>The chosen world folder, set when the dialog returns true.</summary>
    public string? Folder { get; private set; }

    private string? CurrentFolder()
    {
        string name = NameBox.Text.Trim();
        string location = LocationBox.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || location.Length == 0) return null;
        try
        {
            return Path.GetFullPath(Path.Combine(location, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        var folder = CurrentFolder();
        ExportButton.IsEnabled = folder is not null;
        if (folder is null)
            InfoText.Text = "Enter a name that can be used as a folder name, and a location.";
        else if (File.Exists(Path.Combine(folder, WorldFiles.Height)))
            InfoText.Text = $"A world already exists at {folder}. Exporting replaces it.";
        else if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            InfoText.Text = $"{folder} already has files in it; the world files will be added alongside them.";
        else
            InfoText.Text = $"Exports to {folder}";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to export the world", InitialDirectory = LocationBox.Text };
        if (dialog.ShowDialog(this) == true) LocationBox.Text = dialog.FolderName;
    }

    private void GameFolder_Click(object sender, RoutedEventArgs e) => LocationBox.Text = GameInstall.UserGeneratedWorldsDir;

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFolder() is not { } folder) return;
        if (_hasErrors &&
            ThemedMessageBox.Show(this, "The world has errors, so the game may not load it properly. Export it anyway?", "Export World",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        if (File.Exists(Path.Combine(folder, WorldFiles.Height)) &&
            ThemedMessageBox.Show(this, $"Replace the world in '{Path.GetFileName(folder)}'?", "Export World",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        Folder = folder;
        DialogResult = true;
    }
}
