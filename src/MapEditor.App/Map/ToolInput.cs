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
public sealed class ToolInputEventArgs(ToolPhase phase, Point mapPoint, ModifierKeys modifiers) : EventArgs
{
    public ToolPhase Phase { get; } = phase;
    public Point MapPoint { get; } = mapPoint;
    public ModifierKeys Modifiers { get; } = modifiers;
}

/// <summary>Outline shown under the mouse: a circle (brushes) or a rotated square (stamps).</summary>
public sealed record BrushCursor(double SizeMetres, bool Square = false, double RotationDegrees = 0);
