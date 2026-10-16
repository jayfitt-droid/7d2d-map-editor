using System.Windows;
using MapEditor.Core.IO;
using Microsoft.Win32;

namespace MapEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OpenWorld_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a 7 Days to Die world folder" };
        if (dialog.ShowDialog(this) != true)
            return;

        StatusText.Text = "Loading " + dialog.FolderName + "...";
        try
        {
            var world = await Task.Run(() => WorldReader.Read(dialog.FolderName));
            WorldSummary.Text = WorldSummaryFormatter.Format(world);
            StatusText.Text = "Loaded " + world.Name;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to load world";
            MessageBox.Show(this, ex.Message, "Open World", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
