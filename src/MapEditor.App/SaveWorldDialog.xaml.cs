using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapEditor.Core.IO;
using Microsoft.Win32;

namespace MapEditor.App;

/// <summary>Picks a world name and parent folder. The world is saved to Location\Name.</summary>
public partial class SaveWorldDialog : Window
{
    public SaveWorldDialog(string name, string location)
    {
        InitializeComponent();
        NameBox.Text = name;
        LocationBox.Text = location;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

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
        SaveButton.IsEnabled = folder is not null;
        if (folder is null)
            InfoText.Text = "Enter a name that can be used as a folder name, and a location.";
        else if (File.Exists(Path.Combine(folder, WorldFiles.Height)))
            InfoText.Text = $"A world already exists at {folder}. Saving replaces it.";
        else if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            InfoText.Text = $"{folder} already has files in it; the world files will be added alongside them.";
        else
            InfoText.Text = $"Saves to {folder}";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to save the world", InitialDirectory = LocationBox.Text };
        if (dialog.ShowDialog(this) == true) LocationBox.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFolder() is not { } folder) return;
        if (File.Exists(Path.Combine(folder, WorldFiles.Height)) &&
            MessageBox.Show(this, $"Replace the world in '{Path.GetFileName(folder)}'?", "Save World As",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        Folder = folder;
        DialogResult = true;
    }
}
