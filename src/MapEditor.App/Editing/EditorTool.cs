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
}

/// <summary>A tool as listed in the tool panel, with its keyboard shortcut.</summary>
public sealed record ToolItem(EditorTool Tool, string Label, string Shortcut)
{
    public string Display => $"{Label}  ({Shortcut})";
}
