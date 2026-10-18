using System.Windows.Media;

namespace MapEditor.App;

/// <summary>
/// Line icons on a 24 x 24 grid, drawn as stroked paths so they scale with display DPI and take the theme's text
/// colour. Used by the ribbon, the quick-access bar and the status bar.
/// </summary>
public static class Icons
{
    public static Geometry Pan { get; } = Make("M12,2 V22 M2,12 H22 M9,5 L12,2 L15,5 M9,19 L12,22 L15,19 M5,9 L2,12 L5,15 M19,9 L22,12 L19,15");
    public static Geometry Raise { get; } = Make("M2,21 L9,10 L13,15 L16,12 L22,21 Z M18,9 V2 M15,5 L18,2 L21,5");
    public static Geometry Lower { get; } = Make("M2,10 L9,21 L13,16 L16,19 L22,10 M18,2 V9 M15,6 L18,9 L21,6");
    public static Geometry Flatten { get; } = Make("M2,18 H22 M6,4 V12 M3,9 L6,12 L9,9 M18,4 V12 M15,9 L18,12 L21,9");
    public static Geometry Smooth { get; } = Make("M2,15 C6,8 10,8 12,12 C14,16 18,16 22,9 M2,20 H22");
    public static Geometry Roughen { get; } = Make("M2,15 L5,8 L7,13 L10,5 L12,14 L15,7 L17,12 L19,6 L22,14 M2,20 H22");
    public static Geometry Stamp { get; } = Make("M9,2 H15 V8 H9 Z M12,8 V12 M4,12 H20 V17 H4 Z M3,21 H21");
    public static Geometry Biome { get; } = Make("M12,2 L5,12 H8.5 L4,18 H20 L15.5,12 H19 Z M12,18 V22");
    public static Geometry Radiation { get; } = Make("M12,12 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 M12,2 a10,10 0 1,0 0.01,0 Z M12,10 L9,4.5 M13.7,13 L19.7,13 M10.3,13 L5,16.5");
    public static Geometry Water { get; } = Make("M12,2 C12,2 5,10.5 5,15 A7,7 0 0,0 19,15 C19,10.5 12,2 12,2 Z M8.5,15.5 A3.5,3.5 0 0,0 12,19");
    public static Geometry Road { get; } = Make("M8,2 L3,22 M16,2 L21,22 M12,3 V6 M12,10 V13 M12,17 V21");
    public static Geometry Prefab { get; } = Make("M2,11 L12,3 L22,11 M5,9 V21 H19 V9 M10,21 V15 H14 V21");
    public static Geometry Town { get; } = Make("M2,21 H22 M4,21 V10 H10 V21 M10,21 V4 H19 V21 M6,13 H8 M6,17 H8 M13,8 H16 M13,12 H16 M13,16 H16");
    public static Geometry Trader { get; } = Make("M3,3 H21 L22,9 H2 Z M2,9 C2,11.5 7,11.5 7,9 C7,11.5 12,11.5 12,9 C12,11.5 17,11.5 17,9 C17,11.5 22,11.5 22,9 M4,11 V21 H20 V11 M9,21 V15 H15 V21");
    public static Geometry Spawn { get; } = Make("M12,2 a3.5,3.5 0 1,0 0.01,0 Z M5,22 V18 C5,13 19,13 19,18 V22");
    public static Geometry Save { get; } = Make("M3,3 H17 L21,7 V21 H3 Z M7,3 V9 H16 V3 M7,21 V14 H17 V21");
    public static Geometry Undo { get; } = Make("M8,4 L3,9 L8,14 M3,9 H15 A6,6 0 0,1 15,21 H9");
    public static Geometry Redo { get; } = Make("M16,4 L21,9 L16,14 M21,9 H9 A6,6 0 0,0 9,21 H15");
    public static Geometry Layers { get; } = Make("M12,3 L22,8 L12,13 L2,8 Z M2,12 L12,17 L22,12 M2,16 L12,21 L22,16");
    public static Geometry Validate { get; } = Make("M12,2 L20,5 V11 C20,16 16,20 12,22 C8,20 4,16 4,11 V5 Z M8,12 L11,15 L16,9");
    public static Geometry Export { get; } = Make("M12,3 V15 M7,8 L12,3 L17,8 M4,13 V21 H20 V13");
    public static Geometry Fit { get; } = Make("M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M8,8 H16 V16 H8 Z");
    public static Geometry Cursor { get; } = Make("M5,3 L5,19 L9,15 L12,21 L14.5,20 L11.5,14 L17,14 Z");
    public static Geometry Selection { get; } = Make("M3,3 H7 M10,3 H14 M17,3 H21 V7 M21,10 V14 M21,17 V21 H17 M14,21 H10 M7,21 H3 V17 M3,14 V10 M3,7 V3");
    public static Geometry WorldSize { get; } = Make("M3,3 H21 V21 H3 Z M3,9 H21 M9,3 V21");
    public static Geometry Panel { get; } = Make("M3,3 H21 V21 H3 Z M15,3 V21");
    public static Geometry ZoomIn { get; } = Make("M10,3 a7,7 0 1,0 0.01,0 Z M15,15 L21,21 M7,10 H13 M10,7 V13");
    public static Geometry ZoomOut { get; } = Make("M10,3 a7,7 0 1,0 0.01,0 Z M15,15 L21,21 M7,10 H13");
    public static Geometry ChevronUp { get; } = Make("M5,15 L12,8 L19,15");
    public static Geometry ChevronDown { get; } = Make("M5,9 L12,16 L19,9");
    public static Geometry Size { get; } = Make("M4,20 L20,4 M4,14 L14,4 M10,20 L20,10");
    public static Geometry Strength { get; } = Make("M12,3 C12,3 6,10 6,14 A6,6 0 0,0 18,14 C18,10 12,3 12,3 Z M12,20 V12");

    private static Geometry Make(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }
}
