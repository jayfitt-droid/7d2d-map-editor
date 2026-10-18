using System.Windows.Media;

namespace MapEditor.App;

public enum EditorTool
{
    Pan,
    Raise,
    Lower,
    Flatten,
    Smooth,
    Noise,
    Stamp,
    Biome,
    Radiation,
    Water,
    Road,
    Prefab,
    Spawn,
    Town,
}

/// <summary>
/// A tool as shown on the ribbon: its icon, keyboard shortcut and a one-line hint shown under the ribbon while it
/// is active.
/// </summary>
public sealed record ToolItem(EditorTool Tool, string Label, string Shortcut, Geometry Icon, string Hint)
{
    public string Display => $"{Label}  ({Shortcut})";

    public string ToolTip => $"{Label} ({Shortcut})\n{Hint}";
}
