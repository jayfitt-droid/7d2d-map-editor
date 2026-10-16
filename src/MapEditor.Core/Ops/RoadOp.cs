using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>
/// A road to draw: control points in grid metres (x east, z north), the surface, and the half-width of
/// the drivable surface. RWG uses 9 m for four-lane highways and 4.5 m for two-lane country roads.
/// </summary>
public sealed record RoadPath(IReadOnlyList<Vector2> Points, RoadType Type, float Radius, bool Grade = true)
{
    /// <summary>Distance over which graded terrain blends back into the original ground.</summary>
    public float BlendDistance => Type == RoadType.Asphalt ? 10f : 6f;

    /// <summary>Asphalt roads this wide get a 1 m gravel shoulder at the edge, as RWG highways do.</summary>
    public bool HasShoulder => Type == RoadType.Asphalt && Radius >= 4f;
}

/// <summary>
/// Turns a road polyline into splat3 road pixels and, optionally, a graded roadbed: a smoothed height
/// profile along the road, held across its width and blended back into the terrain beyond its edge.
/// </summary>
public static class RoadOp
{
    /// <summary>Spacing of the points the control polyline is resampled to, in metres.</summary>
    public const float SampleSpacing = 4f;

    /// <summary>How far (in metres) the height profile is smoothed along the road for each surface.</summary>
    private static float ProfileSmoothing(RoadType type) => type == RoadType.Asphalt ? 60f : 16f;

    /// <summary>How far (in metres) corners are rounded off; highways sweep in wider curves.</summary>
    public static float CornerRounding(RoadType type) => type == RoadType.Asphalt ? 30f : 15f;

    /// <summary>Resamples the control points and rounds off corners; the end points stay put.</summary>
    public static List<Vector2> Centreline(IReadOnlyList<Vector2> points, float rounding = 15f)
    {
        var line = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            if (i == 0) { line.Add(points[0]); continue; }
            var a = points[i - 1];
            var b = points[i];
            float length = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / SampleSpacing));
            for (int s = 1; s <= steps; s++) line.Add(Vector2.Lerp(a, b, s / (float)steps));
        }
        if (line.Count < 3) return line;

        // Repeated [1/4 1/2 1/4] averaging spreads like a Gaussian of sigma ~ spacing * sqrt(n/2), turning
        // sharp joins into curves about that wide. Short roads are capped so they do not shrink away.
        float sigma = rounding / SampleSpacing;
        int passes = Math.Min((int)(2 * sigma * sigma), line.Count * 2);
        var copy = new Vector2[line.Count];
        for (int pass = 0; pass < passes; pass++)
        {
            line.CopyTo(copy);
            for (int i = 1; i < line.Count - 1; i++)
                line[i] = copy[i] * 0.5f + (copy[i - 1] + copy[i + 1]) * 0.25f;
        }
        return line;
    }

    /// <summary>
    /// Road surface height at each centreline point: the ground under it, smoothed along the road, and kept
    /// at least 1 m above any water it crosses.
    /// </summary>
    public static float[] HeightProfile(World world, IReadOnlyList<Vector2> line, RoadType type)
    {
        var heights = new float[line.Count];
        var floor = new float[line.Count];
        var wet = new bool[line.Count];
        for (int i = 0; i < line.Count; i++)
        {
            int x = Math.Clamp((int)line[i].X, 0, world.Size - 1), z = Math.Clamp((int)line[i].Y, 0, world.Size - 1);
            byte water = world.Water?[x, z] ?? 0;
            heights[i] = World.HeightToMetres(world.Height[x, z]);
            floor[i] = water > 0 ? water + 1 : 0;
            wet[i] = water > 0 && heights[i] < water + 0.5f;
        }

        // Over water the road rides a causeway carried straight from one bank to the other, like a bridge
        // deck, so neither the lake bed nor the water level pulls the approaches down into the land.
        for (int i = 0; i < line.Count; i++)
        {
            if (!wet[i]) continue;
            int end = i;
            while (end + 1 < line.Count && wet[end + 1]) end++;
            float? before = i > 0 ? heights[i - 1] : null;
            float? after = end + 1 < line.Count ? heights[end + 1] : null;
            for (int j = i; j <= end; j++)
            {
                float t = (j - i + 1f) / (end - i + 2f);
                float deck = (before, after) switch
                {
                    ({ } a, { } b) => a + (b - a) * t,
                    ({ } a, null) => a,
                    (null, { } b) => b,
                    _ => floor[j],
                };
                heights[j] = Math.Max(deck, floor[j]);
            }
            i = end;
        }
        for (int i = 0; i < line.Count; i++) heights[i] = Math.Max(heights[i], floor[i]);

        // A 3-tap average applied n times spreads like a Gaussian of sigma ~ spacing * sqrt(2n/3).
        // Holding the floor on every pass makes the road ramp smoothly up to a causeway, not step onto it.
        float sigma = ProfileSmoothing(type) / SampleSpacing;
        int passes = Math.Max(1, (int)(1.5f * sigma * sigma));
        var copy = new float[heights.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            heights.CopyTo(copy, 0);
            for (int i = 1; i < heights.Length - 1; i++)
                heights[i] = Math.Max((copy[i - 1] + copy[i] + copy[i + 1]) / 3f, floor[i]);
        }
        return heights;
    }

    /// <summary>Draws the road into <see cref="World.Roads"/> (and grades the terrain if asked). Returns the changed area.</summary>
    public static GridRect Draw(World world, RoadPath road, EditSession session)
    {
        if (road.Points.Count < 2 || road.Radius <= 0) return GridRect.Empty;
        world.Roads ??= new Grid<byte>(world.Size, world.Size);
        var line = Centreline(road.Points, CornerRounding(road.Type));
        var profile = HeightProfile(world, line, road.Type);
        float reach = road.Radius + (road.Grade ? road.BlendDistance : 0);

        // For every pixel near the road, the distance to the nearest point on the centreline and the road
        // height there. Segments overlap at joins, so the nearest one wins.
        var nearest = new Dictionary<int, (float Distance, float Height)>();
        var bounds = GridRect.Empty;
        int size = world.Size;
        for (int i = 0; i < line.Count - 1; i++)
        {
            var a = line[i];
            var b = line[i + 1];
            var rect = new GridRect(
                (int)MathF.Floor(MathF.Min(a.X, b.X) - reach), (int)MathF.Floor(MathF.Min(a.Y, b.Y) - reach),
                (int)MathF.Ceiling(MathF.Max(a.X, b.X) + reach) + 1, (int)MathF.Ceiling(MathF.Max(a.Y, b.Y) + reach) + 1).Clamp(size, size);
            if (rect.IsEmpty) continue;
            bounds = bounds.Union(rect);
            var ab = b - a;
            float lengthSq = Math.Max(ab.LengthSquared(), 1e-6f);
            for (int z = rect.Z0; z < rect.Z1; z++)
                for (int x = rect.X0; x < rect.X1; x++)
                {
                    var p = new Vector2(x + 0.5f, z + 0.5f);
                    float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d >= reach) continue;
                    int key = x + z * size;
                    if (nearest.TryGetValue(key, out var seen) && seen.Distance <= d) continue;
                    nearest[key] = (d, profile[i] + (profile[i + 1] - profile[i]) * t);
                }
        }
        if (bounds.IsEmpty) return GridRect.Empty;

        // Snapshot per segment so a long diagonal road does not copy the whole box around it.
        for (int i = 0; i < line.Count - 1; i++)
        {
            var rect = new GridRect(
                (int)MathF.Floor(MathF.Min(line[i].X, line[i + 1].X) - reach), (int)MathF.Floor(MathF.Min(line[i].Y, line[i + 1].Y) - reach),
                (int)MathF.Ceiling(MathF.Max(line[i].X, line[i + 1].X) + reach) + 1, (int)MathF.Ceiling(MathF.Max(line[i].Y, line[i + 1].Y) + reach) + 1);
            session.Touch(world.Roads, rect);
            if (road.Grade) session.Touch(world.Height, rect);
        }

        var roads = world.Roads.Data;
        var height = world.Height.Data;
        float core = road.HasShoulder ? road.Radius - 1 : road.Radius;
        foreach (var (key, (d, roadHeight)) in nearest)
        {
            if (d <= road.Radius)
            {
                var type = d <= core ? road.Type : RoadType.Gravel;
                // Asphalt wins where roads meet, so a gravel road joining a highway does not cut through it.
                if (roads[key] != (byte)RoadType.Asphalt || type == RoadType.Asphalt) roads[key] = (byte)type;
            }
            if (!road.Grade) continue;
            float blend = Math.Clamp((d - road.Radius) / road.BlendDistance, 0, 1);
            float ground = World.HeightToMetres(height[key]);
            height[key] = TerrainBrush.ToRaw(roadHeight + (ground - roadHeight) * blend * blend);
        }
        return bounds;
    }

    /// <summary>Removes road pixels under the brush tip; the terrain is left as it is.</summary>
    public static GridRect Erase(World world, float cx, float cz, BrushTip tip, EditSession session)
    {
        if (world.Roads is not { } roads) return GridRect.Empty;
        var rect = tip.Bounds(cx, cz).Clamp(roads.Width, roads.Height);
        bool touched = false;
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var row = roads.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
            {
                if (row[x] == 0 || tip.Distance(x - cx, z - cz) >= 1) continue;
                if (!touched)
                {
                    session.Touch(roads, rect);
                    touched = true;
                }
                row[x] = 0;
            }
        }
        return touched ? rect : GridRect.Empty;
    }

    /// <summary>Length of the polyline in metres.</summary>
    public static float Length(IReadOnlyList<Vector2> points)
    {
        float length = 0;
        for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }
}
