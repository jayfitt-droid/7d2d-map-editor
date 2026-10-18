using Velopack;

namespace MapEditor.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Velopack's install, update and uninstall hooks run first; when the installer starts the app for one of
        // them, Run() does the work and exits.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
