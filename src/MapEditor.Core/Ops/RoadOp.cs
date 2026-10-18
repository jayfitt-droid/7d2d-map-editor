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

    /// <summary>
    /// How far, in metres each way, <see cref="Resmooth"/> averages a road's height along it: a 151 m box, which wipes
    /// out the ripple a town's 150 m tiles leave, then a 75 m one to round it off.
    /// </summary>
    public const int ResmoothReach = 112;

    /// <summary>
    /// Evens out roads already in <paramref name="area"/> after the ground there was reshaped, for example by a town's or
    /// a prefab's flattening blend, which leaves a graded road rolling. Each road pixel takes the average height of the
    /// road pixels around it (two box passes, <see cref="ResmoothReach"/> m each way in all), so the road ramps evenly
    /// and stays level across. The change is full inside the area and fades out over the next
    /// <see cref="ResmoothReach"/> m beyond it, so roads join their untouched parts smoothly, and the ground a few
    /// metres beside the road follows it. Road pixels inside <paramref name="keep"/> (the flattened
    /// footprints) are neither moved nor used. Returns the area changed.
    /// </summary>
    public static GridRect Resmooth(World world, GridRect area, IReadOnlyList<GridRect> keep, EditSession session)
    {
        if (world.Roads is not { } roads) return GridRect.Empty;
        area = area.Clamp(world.Size, world.Size);
        if (area.IsEmpty) return GridRect.Empty;
        const int Reach = ResmoothReach, Side = 6;
        var outer = new GridRect(area.X0 - Reach, area.Z0 - Reach, area.X1 + Reach, area.Z1 + Reach).Clamp(world.Size, world.Size);
        var work = new GridRect(outer.X0 - Reach, outer.Z0 - Reach, outer.X1 + Reach, outer.Z1 + Reach).Clamp(world.Size, world.Size);
        int w = work.X1 - work.X0, d = work.Z1 - work.Z0;

        // The kept footprints as a lookup, since a big town has many.
        var kept = new bool[w * d];
        foreach (var k in keep)
        {
            var r = new GridRect(k.X0 - work.X0, k.Z0 - work.Z0, k.X1 - work.X0, k.Z1 - work.Z0).Clamp(w, d);
            for (int z = r.Z0; z < r.Z1; z++) Array.Fill(kept, true, z * w + r.X0, Math.Max(0, r.X1 - r.X0));
        }
        var mask = new bool[w * d];
        var height = new double[w * d];
        bool any = false;
        for (int z = 0; z < d; z++)
        {
            var row = roads.Row(work.Z0 + z);
            for (int x = 0; x < w; x++)
            {
                int gx = work.X0 + x, gz = work.Z0 + z;
                height[z * w + x] = World.HeightToMetres(world.Height[gx, gz]);
                if (row[gx] == 0 || kept[z * w + x]) continue;
                mask[z * w + x] = true;
                any |= outer.Contains(gx, gz);
            }
        }
        if (!any) return GridRect.Empty;

        var smooth = MaskedMean(height, mask, w, d, 75);
        smooth = MaskedMean(smooth, mask, w, d, Reach - 75);
        var delta = new double[w * d];
        for (int i = 0; i < delta.Length; i++)
        {
            if (!mask[i]) continue;
            int gx = work.X0 + i % w, gz = work.Z0 + i / w;
            if (!outer.Contains(gx, gz)) continue;
            // Full strength in the area, fading to none a reach beyond it.
            int outside = Math.Max(Math.Max(area.X0 - gx, gx - (area.X1 - 1)), Math.Max(area.Z0 - gz, gz - (area.Z1 - 1)));
            delta[i] = (smooth[i] - height[i]) * Math.Clamp(1 - outside / (double)Reach, 0, 1);
        }
        // Beside the road, the ground follows by the road's average change nearby, fading out over a few metres.
        var (sideDelta, cover) = MaskedMeanWithCover(delta, mask, w, d, Side);

        session.Touch(world.Height, outer);
        for (int i = 0; i < delta.Length; i++)
        {
            int gx = work.X0 + i % w, gz = work.Z0 + i / w;
            if (!outer.Contains(gx, gz) || kept[i]) continue;
            double change = mask[i] ? delta[i] : sideDelta[i] * Math.Min(1, cover[i] * 2.5);
            if (change != 0) world.Height[gx, gz] = World.MetresToHeight((float)(height[i] + change));
        }
        return outer;
    }

    /// <summary>A road pixel this close (in metres) to a footprint counts as meeting it.</summary>
    private const float JoinTouch = 3;

    /// <summary>Ramps are at least this long (in metres) and at most <see cref="RampMax"/>; in between, 1 m of height per <see cref="RampRun"/> m.</summary>
    private const float RampMin = 40, RampMax = 150, RampRun = 25;

    /// <summary>
    /// Ramps roads that meet flattened <paramref name="footprints"/> (a POI, or a town's tiles) up or down to the
    /// footprints' ground, <paramref name="height"/> metres, so they run into it without a step. Only roads that reach
    /// within <see cref="JoinTouch"/> m of a footprint are changed, and only the stretch joined to that point: each
    /// pixel moves toward the ground height in full at the edge, fading out (smoothstep) over a ramp whose length
    /// grows with the height gap. The ground a few metres beside the road follows it, and the footprints themselves
    /// are left alone. Returns the area changed.
    /// </summary>
    public static GridRect RampToFootprints(World world, IReadOnlyList<GridRect> footprints, float height, EditSession session)
    {
        if (world.Roads is not { } roads || footprints.Count == 0) return GridRect.Empty;
        var bounds = footprints.Aggregate(GridRect.Empty, (r, f) => r.Union(f));
        const int Side = 6;
        int reach = (int)RampMax + Side + 1;
        var work = new GridRect(bounds.X0 - reach, bounds.Z0 - reach, bounds.X1 + reach, bounds.Z1 + reach).Clamp(world.Size, world.Size);
        if (work.IsEmpty) return GridRect.Empty;
        int w = work.X1 - work.X0, d = work.Z1 - work.Z0;

        // Distance from each pixel to the nearest footprint (0 inside one).
        var distance = new float[w * d];
        Array.Fill(distance, float.MaxValue);
        foreach (var f in footprints)
            for (int z = 0; z < d; z++)
            {
                int gz = work.Z0 + z, dz = gz < f.Z0 ? f.Z0 - gz : gz >= f.Z1 ? gz - (f.Z1 - 1) : 0;
                if (dz > reach) continue;
                for (int x = Math.Max(0, f.X0 - reach - work.X0); x < Math.Min(w, f.X1 + reach - work.X0); x++)
                {
                    int gx = work.X0 + x, dx = gx < f.X0 ? f.X0 - gx : gx >= f.X1 ? gx - (f.X1 - 1) : 0;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist < distance[z * w + x]) distance[z * w + x] = dist;
                }
            }

        bool IsRoad(int i) => roads[work.X0 + i % w, work.Z0 + i / w] != 0 && distance[i] > 0;
        float H(int i) => World.HeightToMetres(world.Height[work.X0 + i % w, work.Z0 + i / w]);

        // The road pixels touching a footprint, and how far the road sits above or below its ground there.
        var touching = new List<int>();
        for (int i = 0; i < w * d; i++)
            if (distance[i] <= JoinTouch && IsRoad(i)) touching.Add(i);
        if (touching.Count == 0) return GridRect.Empty;
        var gaps = touching.Select(i => H(i) - height).OrderBy(g => g).ToList();
        float gap = gaps[gaps.Count / 2];
        if (MathF.Abs(gap) < 0.05f) return GridRect.Empty;
        float ramp = Math.Clamp(MathF.Abs(gap) * RampRun, RampMin, RampMax);

        // Only the road joined to the touching pixels, within the ramp; other roads nearby are left alone.
        var joined = new bool[w * d];
        var queue = new Queue<int>(touching);
        foreach (var i in touching) joined[i] = true;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, z = i / w;
            for (int nz = Math.Max(0, z - 1); nz <= Math.Min(d - 1, z + 1); nz++)
                for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
                {
                    int n = nz * w + nx;
                    if (joined[n] || distance[n] >= ramp || !IsRoad(n)) continue;
                    joined[n] = true;
                    queue.Enqueue(n);
                }
        }

        var heights = new double[w * d];
        var delta = new double[w * d];
        for (int i = 0; i < w * d; i++)
        {
            heights[i] = H(i);
            if (!joined[i]) continue;
            float t = Math.Clamp((distance[i] - JoinTouch) / (ramp - JoinTouch), 0, 1);
            float keep = t * t * (3 - 2 * t);
            delta[i] = (height - heights[i]) * (1 - keep);
        }
        // Beside the road, the ground follows by the road's average change nearby, fading out over a few metres.
        var (sideDelta, cover) = MaskedMeanWithCover(delta, joined, w, d, Side);

        session.Touch(world.Height, work);
        var changed = GridRect.Empty;
        for (int i = 0; i < w * d; i++)
        {
            // The footprints stay as flattened, and other roads stay as they are.
            if (distance[i] == 0 || !joined[i] && IsRoad(i)) continue;
            double change = joined[i] ? delta[i] : sideDelta[i] * Math.Min(1, cover[i] * 2.5);
            if (Math.Abs(change) < 1e-4) continue;
            int gx = work.X0 + i % w, gz = work.Z0 + i / w;
            world.Height[gx, gz] = World.MetresToHeight((float)(heights[i] + change));
            changed = changed.Union(new GridRect(gx, gz, gx + 1, gz + 1));
        }
        return changed;
    }

    /// <summary>The mean of <paramref name="values"/> over the masked cells in a box of radius <paramref name="r"/>; unmasked cells keep their value.</summary>
    private static double[] MaskedMean(double[] values, bool[] mask, int w, int d, int r)
    {
        var (mean, _) = MaskedMeanWithCover(values, mask, w, d, r);
        for (int i = 0; i < mean.Length; i++)
            if (!mask[i]) mean[i] = values[i];
        return mean;
    }

    /// <summary>The masked mean in a box of radius <paramref name="r"/> for every cell, and the share of the box that is masked.</summary>
    private static (double[] Mean, double[] Cover) MaskedMeanWithCover(double[] values, bool[] mask, int w, int d, int r)
    {
        // Summed-area tables of the masked values and of the mask, one row and column bigger.
        var sum = new double[(w + 1) * (d + 1)];
        var count = new int[(w + 1) * (d + 1)];
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x, s = (z + 1) * (w + 1) + x + 1;
                sum[s] = (mask[i] ? values[i] : 0) + sum[s - 1] + sum[s - (w + 1)] - sum[s - (w + 1) - 1];
                count[s] = (mask[i] ? 1 : 0) + count[s - 1] + count[s - (w + 1)] - count[s - (w + 1) - 1];
            }
        var mean = new double[w * d];
        var cover = new double[w * d];
        for (int z = 0; z < d; z++)
        {
            int z0 = Math.Max(0, z - r), z1 = Math.Min(d, z + r + 1);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w, x + r + 1);
                int a = z1 * (w + 1) + x1, b = z0 * (w + 1) + x1, c = z1 * (w + 1) + x0, e = z0 * (w + 1) + x0;
                int n = count[a] - count[b] - count[c] + count[e];
                if (n == 0) continue;
                mean[z * w + x] = (sum[a] - sum[b] - sum[c] + sum[e]) / n;
                cover[z * w + x] = n / (double)((2 * r + 1) * (2 * r + 1));
            }
        }
        return (mean, cover);
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
