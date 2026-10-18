using System.Numerics;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>
/// The open end of a painted road: where its centreline stops (the round cap's centre, so a road drawn on from here
/// joins it without a seam), the way the road was heading as it ended, and its surface and half-width.
/// </summary>
public sealed record RoadEnd(Vector2 Point, Vector2 Direction, RoadType Type, float Radius);

/// <summary>Finds road ends in <see cref="World.Roads"/>, so the road tool can carry a road on from where it stops.</summary>
public static class RoadEnds
{
    private const float Step = 2f;

    /// <summary>
    /// The road end nearest <paramref name="near"/> within <paramref name="reach"/> metres, or null. Works from the
    /// road pixels alone, so it finds ends of roads from RWG and opened worlds as well as ones drawn in the editor:
    /// it follows the road's centreline both ways from the road pixel nearest the point until the road runs out.
    /// </summary>
    public static RoadEnd? Find(Grid<byte> roads, Vector2 near, float reach)
    {
        if (NearestRoad(roads, near, reach) is not { } start) return null;
        if (TownGenerator.MeasureRoad(roads, start) is not { } measured) return null;
        var (type, radius) = measured;
        if (Axis(roads, start, (int)MathF.Max(12, 2 * radius + 6)) is not { } along) return null;

        float limit = reach + 2 * radius + 40;
        RoadEnd? best = null;
        float bestDistance = reach;
        foreach (var sign in new[] { 1f, -1f })
        {
            var dir = along * sign;
            var p = Recentre(roads, start, dir, radius);
            for (float travelled = 0; travelled < limit; travelled += Step)
            {
                var next = p + dir * Step;
                if (!IsRoad(At(roads, next)))
                {
                    // Out to the very tip of the cap, then back by the half-width to where the centreline stopped.
                    var tip = p;
                    for (int i = 0; i < 2 * Step && IsRoad(At(roads, tip + dir * 0.5f)); i++) tip += dir * 0.5f;
                    var end = tip - dir * radius;
                    // The heading of the road body just behind the cap, which the cap and the recentring don't skew.
                    var body = end - dir * (radius + 8);
                    if (Axis(roads, body, (int)(radius + 8)) is { } axis) dir = Vector2.Dot(axis, dir) >= 0 ? axis : -axis;
                    float d = Vector2.Distance(end, near);
                    if (d <= bestDistance)
                    {
                        bestDistance = d;
                        best = new RoadEnd(end, dir, type, radius);
                    }
                    break;
                }
                var moved = Recentre(roads, next, dir, radius);
                if (Vector2.Distance(moved, p) > 0.5f) dir = Vector2.Normalize(dir * 0.7f + Vector2.Normalize(moved - p) * 0.3f);
                p = moved;
            }
        }
        return best;
    }

    private static bool IsRoad(byte b) => b is (byte)RoadType.Asphalt or (byte)RoadType.Gravel;

    private static byte At(Grid<byte> roads, Vector2 p)
    {
        int x = (int)MathF.Floor(p.X), z = (int)MathF.Floor(p.Y);
        return x < 0 || z < 0 || x >= roads.Width || z >= roads.Height ? (byte)0 : roads[x, z];
    }

    private static Vector2? NearestRoad(Grid<byte> roads, Vector2 near, float reach)
    {
        int r = (int)MathF.Ceiling(reach), px = (int)near.X, pz = (int)near.Y;
        Vector2? best = null;
        float bestSq = reach * reach;
        for (int z = Math.Max(0, pz - r); z <= Math.Min(roads.Height - 1, pz + r); z++)
        {
            var row = roads.Row(z);
            for (int x = Math.Max(0, px - r); x <= Math.Min(roads.Width - 1, px + r); x++)
            {
                if (!IsRoad(row[x])) continue;
                var p = new Vector2(x + 0.5f, z + 0.5f);
                float sq = Vector2.DistanceSquared(p, near);
                if (sq < bestSq) (best, bestSq) = (p, sq);
            }
        }
        return best;
    }

    /// <summary>The long axis of the road pixels within <paramref name="reach"/> m of the point (either way along it).</summary>
    private static Vector2? Axis(Grid<byte> roads, Vector2 at, int reach)
    {
        int px = (int)at.X, pz = (int)at.Y;
        double n = 0, sx = 0, sz = 0, sxx = 0, szz = 0, sxz = 0;
        for (int z = Math.Max(0, pz - reach); z <= Math.Min(roads.Height - 1, pz + reach); z++)
        {
            var row = roads.Row(z);
            for (int x = Math.Max(0, px - reach); x <= Math.Min(roads.Width - 1, px + reach); x++)
            {
                if (!IsRoad(row[x]) || (x - px) * (x - px) + (z - pz) * (z - pz) > reach * reach) continue;
                n++;
                sx += x; sz += z;
                sxx += (double)x * x; szz += (double)z * z; sxz += (double)x * z;
            }
        }
        if (n < 8) return null;
        double cxx = sxx / n - sx / n * (sx / n), czz = szz / n - sz / n * (sz / n), cxz = sxz / n - sx / n * (sz / n);
        float angle = 0.5f * (float)Math.Atan2(2 * cxz, cxx - czz);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>
    /// Moves a point on the road to the middle of the road straight across it. Where the run across is far wider
    /// than the road (a junction or a town square), the point stays put.
    /// </summary>
    private static Vector2 Recentre(Grid<byte> roads, Vector2 p, Vector2 dir, float radius)
    {
        if (!IsRoad(At(roads, p))) return p;
        var across = new Vector2(-dir.Y, dir.X);
        float max = 3 * radius + 4, lo = 0, hi = 0;
        while (hi < max && IsRoad(At(roads, p + across * (hi + 0.5f)))) hi += 0.5f;
        while (lo < max && IsRoad(At(roads, p - across * (lo + 0.5f)))) lo += 0.5f;
        if (hi >= max || lo >= max) return p;
        return p + across * ((hi - lo) / 2);
    }
}
