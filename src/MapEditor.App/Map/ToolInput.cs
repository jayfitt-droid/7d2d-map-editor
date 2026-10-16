using System.Windows;
using System.Windows.Input;

namespace MapEditor.App.Map;

public enum ToolPhase
{
    Down,
    Move,
    Up,
}

/// <summary>A tool press/drag/release at <see cref="MapPoint"/> (metres from the north-west corner).</summary>
public sealed class ToolInputEventArgs(ToolPhase phase, Point mapPoint, ModifierKeys modifiers, int clickCount = 1) : EventArgs
{
    public ToolPhase Phase { get; } = phase;
    public Point MapPoint { get; } = mapPoint;
    public ModifierKeys Modifiers { get; } = modifiers;

    /// <summary>2 for the second press of a double-click.</summary>
    public int ClickCount { get; } = clickCount;
}

/// <summary>A road being drawn: its points in map coordinates and its full width in metres.</summary>
public sealed record RoadPreview(IReadOnlyList<Point> MapPoints, double WidthMetres, bool Asphalt);

/// <summary>Outline shown under the mouse: a circle (brushes) or a rotated square (stamps).</summary>
public sealed record BrushCursor(double SizeMetres, bool Square = false, double RotationDegrees = 0);

/// <summary>
/// A prefab about to be placed or moved: its footprint in map coordinates, the way its front faces
/// (0 north, 1 west, 2 south, 3 east; -1 unknown), whether it has warnings, and the lot it snaps into.
/// </summary>
public sealed record PrefabGhost(Rect MapRect, int Front, bool Warning, Rect? Lot = null, bool LotFits = true);
