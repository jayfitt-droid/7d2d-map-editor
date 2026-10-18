using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MapEditor.App;

public partial class App : Application
{
    public static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "crash.log");

    private bool _reportedError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>Logs UI-thread exceptions and keeps the editor open, so a bug does not lose the user's work.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:u}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging is best effort.
        }

        e.Handled = true;
        // A failing render repeats every frame; tell the user once rather than flooding them with dialogs.
        if (_reportedError) return;
        _reportedError = true;
        ThemedMessageBox.Show(MainWindow, $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to {CrashLogPath}.",
            "7D2D Map Editor", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
