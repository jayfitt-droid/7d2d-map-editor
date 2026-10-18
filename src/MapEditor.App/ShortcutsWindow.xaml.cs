using System.Windows;
using System.Windows.Input;

namespace MapEditor.App;

/// <summary>Help > Keyboard Shortcuts (F1): a reference window that can stay open beside the map.</summary>
public partial class ShortcutsWindow : Window
{
    public sealed record Shortcut(string[] Keys, string Action);

    public sealed record Group(string Title, IReadOnlyList<Shortcut> Items);

    public ShortcutsWindow(IEnumerable<ToolItem> tools)
    {
        InitializeComponent();
        Groups.ItemsSource = new Group[]
        {
            new("Tools", tools.Select(t => new Shortcut([t.Shortcut], t.Label)).ToList()),
            new("Brushes",
            [
                new(["[", "]"], "Smaller / larger"),
                new(["Q", "E"], "Turn (square brush, stamp, prefab, spawn)"),
                new(["Ctrl+click"], "Sample height or water level"),
                new(["Shift+drag"], "Erase (roads, radiation)"),
            ]),
            new("Snapping",
            [
                new(["Alt+click"], "Place a road point, POI or trader without snapping"),
            ]),
            new("Prefabs",
            [
                new(["Del"], "Delete the selection"),
                new(["PgUp", "PgDn"], "Raise or lower one block"),
                new(["Esc"], "Stop placing or deselect"),
            ]),
            new("View",
            [
                new(["Wheel", "+", "-"], "Zoom"),
                new(["Home"], "Fit to window"),
                new(["Right drag"], "Pan with any tool"),
            ]),
            new("File",
            [
                new(["Ctrl+N"], "New world"),
                new(["Ctrl+O"], "Open"),
                new(["Ctrl+S"], "Save"),
                new(["Ctrl+Shift+S"], "Save project as"),
                new(["Ctrl+E"], "Export world"),
                new(["Ctrl+W"], "Close"),
                new(["F7"], "Validate"),
                new(["Ctrl+Z", "Ctrl+Y"], "Undo / redo"),
                new(["F1"], "This list"),
            ]),
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Escape or Key.F1)
        {
            Close();
            e.Handled = true;
        }
    }
}
