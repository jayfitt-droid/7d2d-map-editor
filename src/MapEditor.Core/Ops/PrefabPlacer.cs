using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>Where a prefab would go: its min corner in world coords (centre of the map is 0,0) and rotation.</summary>
public readonly record struct PrefabSpot(int X, int Y, int Z, int Rotation)
{
    public Int3 Position => new(X, Y, Z);
}

/// <summary>A street-tile lot a POI snaps into, with the spot RWG would give the POI there.</summary>
public sealed record LotSnap(PrefabInstance Tile, PrefabMarker Lot, int LotX, int LotZ, bool Fits, PrefabSpot Spot);

/// <summary>
/// Places prefabs the way RWG does. A wilderness POI sits at the median ground height of its footprint
/// plus its YOffset, and the ground is set to that height under it and blended back over 18 m
/// (StreetTile.SmoothTerrainRect). RWG writes dtm.raw 1 m below its working heights, so in dtm.raw terms a
/// prefab's y is the ground + 1 + YOffset. Street tiles sit on a 150 m grid, share a town height and blend over 50 m.
/// On load the game copies each prefab's own terrain blocks into the heightmap, so the flattening only has
/// to make the surroundings meet the prefab.
/// </summary>
public static class PrefabPlacer
{
    public const int PoiBlend = 18;
    public const int StreetTileBlend = 50;
    public const int StreetTileSize = 150;

    /// <summary>RWG rejects wilderness spots whose ground differs from the centre by more than this.</summary>
    public const int MaxGroundVariation = 11;

    /// <summary>The y a prefab gets standing on dtm.raw ground of <paramref name="ground"/> metres.</summary>
    public static int PrefabY(PrefabInfo info, int ground) => ground + 1 + info.YOffset;

    /// <summary>The dtm.raw ground height a prefab at <paramref name="y"/> stands on.</summary>
    public static int GroundUnder(PrefabInfo info, int y) => y - 1 - info.YOffset;

    /// <summary>A footprint in grid cells (x east, z north from the south-west corner).</summary>
    public static GridRect Footprint(World world, PrefabInfo info, int x, int z, int rotation)
    {
        var (sx, sz) = info.Footprint(rotation);
        int gx = world.WorldToGrid(x), gz = world.WorldToGrid(z);
        return new GridRect(gx, gz, gx + sx, gz + sz);
    }

    public static GridRect Footprint(World world, PrefabInstance p, PrefabLibrary library)
    {
        var (sx, sz) = library.Find(p.Name)?.Footprint(p.Rotation) ?? (1, 1);
        int gx = world.WorldToGrid(p.Position.X), gz = world.WorldToGrid(p.Position.Z);
        return new GridRect(gx, gz, gx + sx, gz + sz);
    }

    /// <summary>The median of the rounded-up ground heights in <paramref name="cells"/>, as RWG computes it.</summary>
    public static int MedianGround(World world, GridRect cells)
    {
        cells = cells.Clamp(world.Size, world.Size);
        if (cells.IsEmpty) return 0;
        var heights = new List<int>((cells.X1 - cells.X0) * (cells.Z1 - cells.Z0));
        for (int z = cells.Z0; z < cells.Z1; z++)
            for (int x = cells.X0; x < cells.X1; x++)
                heights.Add((int)MathF.Ceiling(World.HeightToMetres(world.Height[x, z])));
        heights.Sort();
        int mid = heights.Count / 2;
        return heights.Count % 2 == 0 ? (heights[mid] + heights[mid - 1]) / 2 : heights[mid];
    }

    /// <summary>How far (in metres) from a road the blend around a flattened footprint fades in, so roads keep their shape.</summary>
    public const int RoadClearance = 6;

    /// <summary>
    /// Sets the ground in <paramref name="cells"/> to <paramref name="height"/> metres and blends it back into
    /// the surrounding terrain with a smoothstep over <paramref name="blend"/> metres. Roads outside the cells are
    /// left as they are, the blend easing in over <see cref="RoadClearance"/> m beside them. Returns the cells changed.
    /// </summary>
    public static GridRect Flatten(World world, GridRect cells, int height, int blend, EditSession session)
    {
        var area = new GridRect(cells.X0 - blend - 1, cells.Z0 - blend - 1, cells.X1 + blend + 1, cells.Z1 + blend + 1)
            .Clamp(world.Size, world.Size);
        if (area.IsEmpty) return GridRect.Empty;
        session.Touch(world.Height, area);
        ushort flat = World.MetresToHeight(height);
        var roadDistance = RoadDistance(world, area, RoadClearance);
        for (int z = area.Z0; z < area.Z1; z++)
        {
            int dz = z < cells.Z0 ? cells.Z0 - z : z >= cells.Z1 ? z - (cells.Z1 - 1) : 0;
            for (int x = area.X0; x < area.X1; x++)
            {
                int dx = x < cells.X0 ? cells.X0 - x : x >= cells.X1 ? x - (cells.X1 - 1) : 0;
                if (dx == 0 && dz == 0)
                {
                    world.Height[x, z] = flat;
                    continue;
                }
                float t = MathF.Sqrt(dx * dx + dz * dz) / blend;
                if (t >= 1) continue;
                float s = t * t * (3 - 2 * t);
                if (roadDistance is not null)
                {
                    float r = Math.Clamp(roadDistance[(z - area.Z0) * (area.X1 - area.X0) + x - area.X0] / RoadClearance, 0, 1);
                    if (r <= 0) continue;
                    s = 1 - (1 - s) * r * r * (3 - 2 * r);
                }
                float current = World.HeightToMetres(world.Height[x, z]);
                world.Height[x, z] = World.MetresToHeight(current * s + height * (1 - s));
            }
        }
        return area;
    }

    /// <summary>
    /// The distance (in metres, up to <paramref name="reach"/>) from each cell of <paramref name="area"/> to the
    /// nearest road pixel, row by row; null when there are no roads near.
    /// </summary>
    private static float[]? RoadDistance(World world, GridRect area, int reach)
    {
        if (world.Roads is not { } roads) return null;
        int w = area.X1 - area.X0, d = area.Z1 - area.Z0;
        float[]? result = null;
        var around = new GridRect(area.X0 - reach, area.Z0 - reach, area.X1 + reach, area.Z1 + reach).Clamp(world.Size, world.Size);
        for (int z = around.Z0; z < around.Z1; z++)
            for (int x = around.X0; x < around.X1; x++)
            {
                if (roads[x, z] == 0) continue;
                if (result is null)
                {
                    result = new float[w * d];
                    Array.Fill(result, float.MaxValue);
                }
                for (int nz = Math.Max(area.Z0, z - reach); nz < Math.Min(area.Z1, z + reach + 1); nz++)
                    for (int nx = Math.Max(area.X0, x - reach); nx < Math.Min(area.X1, x + reach + 1); nx++)
                    {
                        int i = (nz - area.Z0) * w + nx - area.X0;
                        float dist = MathF.Sqrt((nx - x) * (nx - x) + (nz - z) * (nz - z));
                        if (dist < result[i]) result[i] = dist;
                    }
            }
        return result;
    }

    /// <summary>A road this close (in metres) to a footprint meets it, and the prefab takes the road's height.</summary>
    public const int RoadTouch = 3;

    /// <summary>
    /// The ground height (whole metres) of the road that meets <paramref name="cells"/>: the median of the road pixels
    /// within <see cref="RoadTouch"/> m of them, or null when no road comes that close.
    /// </summary>
    public static int? RoadGround(World world, GridRect cells)
    {
        if (world.Roads is not { } roads) return null;
        var near = new GridRect(cells.X0 - RoadTouch, cells.Z0 - RoadTouch, cells.X1 + RoadTouch, cells.Z1 + RoadTouch)
            .Clamp(world.Size, world.Size);
        var heights = new List<float>();
        for (int z = near.Z0; z < near.Z1; z++)
            for (int x = near.X0; x < near.X1; x++)
            {
                if (roads[x, z] == 0) continue;
                int dx = x < cells.X0 ? cells.X0 - x : x >= cells.X1 ? x - (cells.X1 - 1) : 0;
                int dz = z < cells.Z0 ? cells.Z0 - z : z >= cells.Z1 ? z - (cells.Z1 - 1) : 0;
                if (dx * dx + dz * dz > RoadTouch * RoadTouch) continue;
                heights.Add(World.HeightToMetres(world.Height[x, z]));
            }
        if (heights.Count == 0) return null;
        heights.Sort();
        return (int)MathF.Round(heights[heights.Count / 2]);
    }

    /// <summary>
    /// Where the 150 m street tile grid starts (0 to 149 m east and north) near grid point (gx, gz): that of the
    /// nearest street tile within two tiles, else null.
    /// </summary>
    private static (int X, int Z)? TileGridOffset(World world, PrefabLibrary library, float gx, float gz,
        IReadOnlyCollection<PrefabInstance>? ignore)
    {
        (int, int)? best = null;
        float bestD = 2f * StreetTileSize * 2 * StreetTileSize;
        foreach (var p in world.Prefabs)
        {
            if (ignore?.Contains(p) == true || library.Find(p.Name) is not { IsStreetTile: true }) continue;
            int x = world.WorldToGrid(p.Position.X), z = world.WorldToGrid(p.Position.Z);
            float dx = x + StreetTileSize / 2f - gx, dz = z + StreetTileSize / 2f - gz;
            if (dx * dx + dz * dz >= bestD) continue;
            bestD = dx * dx + dz * dz;
            best = (((x % StreetTileSize) + StreetTileSize) % StreetTileSize, ((z % StreetTileSize) + StreetTileSize) % StreetTileSize);
        }
        return best;
    }

    /// <summary>
    /// The spot for <paramref name="info"/> centred on grid point (<paramref name="gx"/>, <paramref name="gz"/>).
    /// Street tiles snap to the 150 m grid of a street tile nearby (unless <paramref name="snapTiles"/> is false) and take
    /// the height of a neighbouring tile; POIs snap into a street-tile lot under the cursor when they fit
    /// (<paramref name="lot"/>). Otherwise a prefab that meets a road takes the road's height, so the road stays as
    /// drawn and meets it level, else it sits on the median ground.
    /// </summary>
    public static PrefabSpot SpotAt(World world, PrefabLibrary library, PrefabInfo info, float gx, float gz, int rotation,
        out LotSnap? lot, IReadOnlyCollection<PrefabInstance>? ignore = null, bool snapTiles = true)
    {
        lot = null;
        rotation &= 3;
        // Towns start their 150 m grid at the corner they were drawn from, so follow the grid of the nearest street
        // tile. With none close by the tile goes where it's put: the game only reads each prefab's position.
        if (info.IsStreetTile && snapTiles && TileGridOffset(world, library, gx, gz, ignore) is var (ox, oz))
        {
            int tx = (int)MathF.Floor((gx - ox) / StreetTileSize) * StreetTileSize + ox;
            int tz = (int)MathF.Floor((gz - oz) / StreetTileSize) * StreetTileSize + oz;
            int x = world.GridToWorld(tx), z = world.GridToWorld(tz);
            var cells = Footprint(world, info, x, z, rotation);
            int ground = NeighbourTileGround(world, library, info, x, z, rotation, ignore)
                         ?? RoadGround(world, cells) ?? MedianGround(world, cells);
            return new PrefabSpot(x, PrefabY(info, ground), z, rotation);
        }

        if (!info.IsPart && !info.IsStreetTile && FindLot(world, library, info, gx, gz, ignore) is { } found)
        {
            lot = found;
            if (found.Fits) return found.Spot;
        }

        var (sx, sz) = info.Footprint(rotation);
        int wx = world.GridToWorld((int)MathF.Round(gx - sx / 2f)), wz = world.GridToWorld((int)MathF.Round(gz - sz / 2f));
        var footprint = Footprint(world, info, wx, wz, rotation);
        int at = (info.IsStreetTile ? NeighbourTileGround(world, library, info, wx, wz, rotation, ignore) : null)
                 ?? (info.IsPart ? null : RoadGround(world, footprint)) ?? MedianGround(world, footprint);
        return new PrefabSpot(wx, PrefabY(info, at), wz, rotation);
    }

    /// <summary>Placed prefabs that are street tiles (<paramref name="tiles"/>) or POIs (not tiles or parts), with their footprints.</summary>
    private static IEnumerable<(PrefabInstance Prefab, GridRect Cells)> PlacedAround(World world, PrefabLibrary library, bool tiles,
        IReadOnlyCollection<PrefabInstance>? ignore)
    {
        foreach (var p in world.Prefabs)
        {
            if (ignore?.Contains(p) == true || library.Find(p.Name) is not { IsPart: false } other || other.IsStreetTile != tiles) continue;
            yield return (p, Footprint(world, p, library));
        }
    }

    /// <summary>
    /// Moves a POI's spot up to <paramref name="snap"/> metres each way so its edges meet the edges of street tiles
    /// nearby: against a tile's side from outside, flush with its side from inside, or lined up with the side of a
    /// tile beside it. Each axis snaps to the nearest such edge on its own, and the height is taken again for the new
    /// ground. Street tiles and parts don't snap.
    /// </summary>
    public static PrefabSpot SnapToTiles(World world, PrefabLibrary library, PrefabInfo info, PrefabSpot spot, int snap,
        IReadOnlyCollection<PrefabInstance>? ignore = null)
    {
        if (info.IsStreetTile || info.IsPart || snap <= 0) return spot;
        var r = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        int? bestX = null, bestZ = null;
        void Try(ref int? best, int delta)
        {
            if (Math.Abs(delta) <= snap && (best is null || Math.Abs(delta) < Math.Abs(best.Value))) best = delta;
        }
        foreach (var (_, o) in PlacedAround(world, library, tiles: true, ignore))
        {
            // How far apart the two are across each axis (0 or less when their ranges overlap).
            int gapX = Math.Max(o.X0 - r.X1, r.X0 - o.X1), gapZ = Math.Max(o.Z0 - r.Z1, r.Z0 - o.Z1);
            if (gapZ <= snap)
            {
                // Level with the tile north to south: against its east or west side, outside or in. Off its north or
                // south end, line up with its sides instead.
                if (gapZ < 0) { Try(ref bestX, o.X1 - r.X0); Try(ref bestX, o.X0 - r.X1); }
                Try(ref bestX, o.X0 - r.X0);
                Try(ref bestX, o.X1 - r.X1);
            }
            if (gapX <= snap)
            {
                if (gapX < 0) { Try(ref bestZ, o.Z1 - r.Z0); Try(ref bestZ, o.Z0 - r.Z1); }
                Try(ref bestZ, o.Z0 - r.Z0);
                Try(ref bestZ, o.Z1 - r.Z1);
            }
        }
        if (bestX is null && bestZ is null) return spot;
        int x = spot.X + (bestX ?? 0), z = spot.Z + (bestZ ?? 0);
        return new PrefabSpot(x, PrefabY(info, MedianGround(world, Footprint(world, info, x, z, spot.Rotation))), z, spot.Rotation);
    }

    /// <summary>
    /// The prefabs of its own kind a POI or street tile at <paramref name="spot"/> covers (their centre lies inside its
    /// footprint), which placing or dropping it there replaces. Empty for parts.
    /// </summary>
    public static List<PrefabInstance> Covered(World world, PrefabLibrary library, PrefabInfo info, PrefabSpot spot,
        IReadOnlyCollection<PrefabInstance>? ignore = null)
    {
        if (info.IsPart) return [];
        var r = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        return PlacedAround(world, library, info.IsStreetTile, ignore)
            .Where(o => o.Cells.X0 + o.Cells.X1 >= 2 * r.X0 && o.Cells.X0 + o.Cells.X1 < 2 * r.X1
                        && o.Cells.Z0 + o.Cells.Z1 >= 2 * r.Z0 && o.Cells.Z0 + o.Cells.Z1 < 2 * r.Z1)
            .Select(o => o.Prefab)
            .ToList();
    }

    /// <summary>
    /// The street tile a tile at <paramref name="spot"/> would partly overlap, which stops it being placed there (one
    /// whose centre it covers is replaced instead, so pass those in <paramref name="ignore"/>). Null for POIs.
    /// </summary>
    public static PrefabInstance? TileInTheWay(World world, PrefabLibrary library, PrefabInfo info, PrefabSpot spot,
        IReadOnlyCollection<PrefabInstance>? ignore = null)
    {
        if (!info.IsStreetTile) return null;
        var r = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        return PlacedAround(world, library, tiles: true, ignore)
            .Where(o => o.Cells.X0 < r.X1 && o.Cells.X1 > r.X0 && o.Cells.Z0 < r.Z1 && o.Cells.Z1 > r.Z0)
            .Select(o => o.Prefab)
            .FirstOrDefault();
    }

    /// <summary>The ground height of a street tile sharing an edge with this one, so streets meet level.</summary>
    private static int? NeighbourTileGround(World world, PrefabLibrary library, PrefabInfo info, int x, int z, int rotation,
        IReadOnlyCollection<PrefabInstance>? ignore)
    {
        var (sx, sz) = info.Footprint(rotation);
        foreach (var p in world.Prefabs)
        {
            if (ignore?.Contains(p) == true || library.Find(p.Name) is not { IsStreetTile: true } other) continue;
            var (ox, oz) = other.Footprint(p.Rotation);
            bool touchX = p.Position.X == x + sx || p.Position.X + ox == x;
            bool touchZ = p.Position.Z == z + sz || p.Position.Z + oz == z;
            bool overlapX = p.Position.X < x + sx && p.Position.X + ox > x;
            bool overlapZ = p.Position.Z < z + sz && p.Position.Z + oz > z;
            if ((touchX && overlapZ) || (touchZ && overlapX)) return GroundUnder(other, p.Position.Y);
        }
        return null;
    }

    /// <summary>
    /// The POI lot of a placed street tile under grid point (gx, gz), and where RWG would put
    /// <paramref name="poi"/> in it (StreetTile.SpawnMarkerPartsAndPrefabs): turned to face the lot's street
    /// and pushed against that side, at the tile's ground level. Tiles offer overlapping lots of different
    /// sizes; the smallest one the POI fits wins, else the largest so the caller can say it is too big.
    /// </summary>
    public static LotSnap? FindLot(World world, PrefabLibrary library, PrefabInfo poi, float gx, float gz,
        IReadOnlyCollection<PrefabInstance>? ignore = null)
    {
        float wx = gx - world.Size / 2f, wz = gz - world.Size / 2f;
        LotSnap? best = null;
        foreach (var tile in world.Prefabs)
        {
            if (ignore?.Contains(tile) == true || library.Find(tile.Name) is not { } info || info.Markers.Count == 0) continue;
            var (tx, tz) = info.Footprint(tile.Rotation);
            if (wx < tile.Position.X || wz < tile.Position.Z || wx >= tile.Position.X + tx || wz >= tile.Position.Z + tz) continue;
            foreach (var m in info.RotatedMarkers(tile.Rotation))
            {
                if (m.Type != MarkerType.POISpawn) continue;
                int lx = tile.Position.X + m.Start.X, lz = tile.Position.Z + m.Start.Z;
                if (wx < lx || wz < lz || wx >= lx + m.Size.X || wz >= lz + m.Size.Z) continue;

                var spot = LotSpot(new PrefabSpot(tile.Position.X, tile.Position.Y, tile.Position.Z, tile.Rotation), m, poi);
                var (sx, sz) = poi.Footprint(spot.Rotation);
                bool fits = sx <= m.Size.X && sz <= m.Size.Z;
                var snap = new LotSnap(tile, m, lx, lz, fits, spot);
                long area = (long)m.Size.X * m.Size.Z, bestArea = best is null ? 0 : (long)best.Lot.Size.X * best.Lot.Size.Z;
                if (best is null || fits && (!best.Fits || area < bestArea) || !fits && !best.Fits && area > bestArea) best = snap;
            }
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>
    /// Where RWG puts <paramref name="poi"/> in lot <paramref name="lot"/> (already turned with its parent) of a
    /// parent prefab at <paramref name="parent"/>: turned to face the lot's street, pushed against that side, and
    /// on the parent's level.
    /// </summary>
    public static PrefabSpot LotSpot(PrefabSpot parent, PrefabMarker lot, PrefabInfo poi)
    {
        int rotation = (parent.Rotation + poi.RotationToFaceNorth + lot.Rotations) & 3;
        var (sx, sz) = poi.Footprint(rotation);
        int x = parent.X + lot.Start.X, z = parent.Z + lot.Start.Z;
        switch (lot.Rotations & 3)
        {
            case 0: x += lot.Size.X / 2 - sx / 2; z += lot.Size.Z - sz; break;
            case 1: z += lot.Size.Z / 2 - sz / 2; break;
            case 2: x += lot.Size.X / 2 - sx / 2; break;
            case 3: z += lot.Size.Z / 2 - sz / 2; x += lot.Size.X - sx; break;
        }
        return new PrefabSpot(x, parent.Y + lot.Start.Y + poi.YOffset, z, rotation);
    }

    /// <summary>Things to warn about for a prefab at <paramref name="spot"/>, each a short phrase.</summary>
    public static List<string> Check(World world, PrefabLibrary library, PrefabInfo info, PrefabSpot spot,
        IReadOnlyCollection<PrefabInstance>? ignore = null, bool snappedToLot = false)
    {
        var problems = new List<string>();
        var cells = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        if (cells.X0 < 0 || cells.Z0 < 0 || cells.X1 > world.Size || cells.Z1 > world.Size) problems.Add("outside the world");
        if (spot.Y < 2) problems.Add("too low (under 2 m)");
        if (info.IsPart) return problems;

        foreach (var p in world.Prefabs)
        {
            if (ignore?.Contains(p) == true || library.Find(p.Name) is not { IsPart: false } other) continue;
            // Tiles clash with tiles, POIs with POIs; a POI on a street tile is a town, not an overlap.
            if (other.IsStreetTile != info.IsStreetTile) continue;
            var o = Footprint(world, p, library);
            if (o.X0 < cells.X1 && o.X1 > cells.X0 && o.Z0 < cells.Z1 && o.Z1 > cells.Z0)
            {
                problems.Add("overlaps " + p.Name);
                break;
            }
        }

        var c = cells.Clamp(world.Size, world.Size);
        if (c.IsEmpty) return problems;
        long wet = 0;
        int lo = int.MaxValue, hi = int.MinValue;
        for (int z = c.Z0; z < c.Z1; z++)
            for (int x = c.X0; x < c.X1; x++)
            {
                float h = World.HeightToMetres(world.Height[x, z]);
                byte water = world.Water?[x, z] ?? 0;
                if (water > 0 && h <= water + 0.5f) wet++;
                int ceil = (int)MathF.Ceiling(h);
                lo = Math.Min(lo, ceil);
                hi = Math.Max(hi, ceil);
            }
        if (wet > 0) problems.Add($"{Math.Max(1, wet * 100 / ((long)(c.X1 - c.X0) * (c.Z1 - c.Z0)))}% under water");
        if (!snappedToLot && hi - lo > MaxGroundVariation) problems.Add($"ground varies by {hi - lo} m");
        return problems;
    }

    /// <summary>
    /// Adds a prefab at <paramref name="spot"/> and, if asked, flattens the ground under it. A POI in a
    /// street-tile lot is left on the tile's ground.
    /// </summary>
    public static PrefabInstance Place(World world, PrefabInfo info, PrefabSpot spot, bool flatten, EditSession session,
        bool snappedToLot = false)
    {
        var instance = new PrefabInstance { Name = info.Name, Position = spot.Position, Rotation = spot.Rotation };
        var cells = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        session.TouchList(world.Prefabs, p => p.Clone(), cells.Clamp(world.Size, world.Size));
        world.Prefabs.Add(instance);
        if (flatten && !snappedToLot && !info.IsPart) FlattenUnder(world, info, cells, spot, session);
        return instance;
    }

    /// <summary>
    /// What lies wholly on <paramref name="prefab"/> and moves, turns and goes with it: the POIs and parts on a
    /// street tile, or the parts (cars, props) on a POI.
    /// </summary>
    public static List<PrefabInstance> Contents(World world, PrefabLibrary library, PrefabInstance prefab)
    {
        if (library.Find(prefab.Name) is not { IsPart: false } info) return [];
        var t = Footprint(world, prefab, library);
        var result = new List<PrefabInstance>();
        foreach (var p in world.Prefabs)
        {
            if (ReferenceEquals(p, prefab) || library.Find(p.Name) is not { IsStreetTile: false } other) continue;
            if (!info.IsStreetTile && !other.IsPart) continue;
            var r = Footprint(world, p, library);
            if (r.X0 >= t.X0 && r.Z0 >= t.Z0 && r.X1 <= t.X1 && r.Z1 <= t.Z1) result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Moves <paramref name="prefab"/> to <paramref name="spot"/>; what lies on it (<see cref="Contents"/>) moves
    /// and turns with it. The ground at the new place is flattened if asked; the old place keeps its flattened ground.
    /// </summary>
    public static void Move(World world, PrefabLibrary library, PrefabInstance prefab, PrefabSpot spot, bool flatten,
        EditSession session, bool snappedToLot = false)
    {
        if (library.Find(prefab.Name) is not { } info) return;
        var contents = Contents(world, library, prefab);
        var before = Footprint(world, prefab, library);
        session.TouchList(world.Prefabs, p => p.Clone(), before.Clamp(world.Size, world.Size));

        var (tx, tz) = info.Footprint(prefab.Rotation);
        int turns = (spot.Rotation - prefab.Rotation) & 3;
        int dy = spot.Y - prefab.Position.Y;
        foreach (var p in contents)
        {
            var (px, pz) = library.Find(p.Name)!.Footprint(p.Rotation);
            // Position on the tile, turned with it one quarter turn (counter-clockwise) at a time.
            int lx = p.Position.X - prefab.Position.X, lz = p.Position.Z - prefab.Position.Z;
            int w = tx, d = tz;
            for (int i = 0; i < turns; i++)
            {
                (lx, lz) = (d - (lz + pz), lx);
                (px, pz) = (pz, px);
                (w, d) = (d, w);
            }
            p.Position = new Int3(spot.X + lx, p.Position.Y + dy, spot.Z + lz);
            p.Rotation = (p.Rotation + turns) & 3;
        }
        prefab.Position = spot.Position;
        prefab.Rotation = spot.Rotation;

        var after = Footprint(world, info, spot.X, spot.Z, spot.Rotation);
        session.TouchList(world.Prefabs, p => p.Clone(), after.Clamp(world.Size, world.Size));
        if (flatten && !snappedToLot && !info.IsPart) FlattenUnder(world, info, after, spot, session);
    }

    /// <summary>
    /// Flattens a placed prefab's footprint and blends it into the ground around; roads outside it are left as drawn
    /// (the prefab takes a road's height instead, in <see cref="SpotAt"/>).
    /// </summary>
    private static void FlattenUnder(World world, PrefabInfo info, GridRect cells, PrefabSpot spot, EditSession session)
    {
        int ground = GroundUnder(info, spot.Y);
        Flatten(world, cells, ground, info.IsStreetTile ? StreetTileBlend : PoiBlend, session);
    }

    /// <summary>Removes a prefab and everything on it (<see cref="Contents"/>). Returns how many were removed.</summary>
    public static int Delete(World world, PrefabLibrary library, PrefabInstance prefab, EditSession session)
    {
        var doomed = Contents(world, library, prefab);
        doomed.Add(prefab);
        var bounds = doomed.Aggregate(GridRect.Empty, (r, p) => r.Union(Footprint(world, p, library)));
        session.TouchList(world.Prefabs, p => p.Clone(), bounds.Clamp(world.Size, world.Size));
        return world.Prefabs.RemoveAll(doomed.Contains);
    }

    /// <summary>
    /// Swaps <paramref name="old"/> for <paramref name="info"/> in the same place, facing the same way and on the
    /// same ground. A POI in a street-tile lot is fitted to the lot again; a street tile keeps the POIs on it.
    /// The old prefab's parts go, since they belong to its markers. Returns the new prefab and the lot it is in.
    /// </summary>
    public static (PrefabInstance Prefab, LotSnap? Lot) Replace(World world, PrefabLibrary library, PrefabInstance old,
        PrefabInfo info, bool flatten, EditSession session)
    {
        var oldInfo = library.Find(old.Name);
        var (ox, oz) = oldInfo?.Footprint(old.Rotation) ?? (1, 1);
        float cx = world.WorldToGrid(old.Position.X) + ox / 2f, cz = world.WorldToGrid(old.Position.Z) + oz / 2f;
        int rotation = (old.Rotation - (oldInfo?.RotationToFaceNorth ?? 0) + info.RotationToFaceNorth) & 3;
        int ground = oldInfo is null ? old.Position.Y - 1 : GroundUnder(oldInfo, old.Position.Y);

        var doomed = Contents(world, library, old).Where(p => library.Find(p.Name)?.IsPart == true).ToList();
        doomed.Add(old);
        var bounds = doomed.Aggregate(GridRect.Empty, (r, p) => r.Union(Footprint(world, p, library)));
        session.TouchList(world.Prefabs, p => p.Clone(), bounds.Clamp(world.Size, world.Size));
        int index = world.Prefabs.IndexOf(old);
        world.Prefabs.RemoveAll(doomed.Contains);

        PrefabSpot spot;
        LotSnap? inLot = null;
        if (info.IsStreetTile)
        {
            spot = new PrefabSpot(old.Position.X, PrefabY(info, ground), old.Position.Z, rotation);
        }
        else if (!info.IsPart && FindLot(world, library, info, cx, cz) is { Fits: true } lot)
        {
            spot = lot.Spot;
            inLot = lot;
        }
        else
        {
            var (sx, sz) = info.Footprint(rotation);
            spot = new PrefabSpot(world.GridToWorld((int)MathF.Round(cx - sx / 2f)), PrefabY(info, ground),
                world.GridToWorld((int)MathF.Round(cz - sz / 2f)), rotation);
        }

        var placed = Place(world, info, spot, flatten, session, inLot is not null);
        // Keep its place in the list, so prefabs.xml only changes on that line.
        world.Prefabs.Remove(placed);
        world.Prefabs.Insert(Math.Clamp(index, 0, world.Prefabs.Count), placed);
        return (placed, inLot);
    }

    /// <summary>
    /// The parts RWG adds from a prefab's part markers (street furniture on tiles, cars and props on POIs): one
    /// part per marker group, picked by the markers' spawn chances, skipping parts tagged for other districts
    /// or overlapping one already chosen. <paramref name="district"/> is the town district the prefab is in.
    /// <paramref name="taken"/> carries the part footprints of a whole street tile across calls, as RWG keeps them.
    /// </summary>
    public static List<PrefabInstance> MarkerParts(PrefabLibrary library, PrefabInfo prefab, PrefabSpot spot, int seed,
        string? district = null, List<(int X0, int Z0, int X1, int Z1)>? taken = null)
    {
        district ??= prefab.District;
        var rand = new Random(seed);
        var result = new List<PrefabInstance>();
        taken ??= [];
        var markers = prefab.RotatedMarkers(spot.Rotation).Where(m => m.Type == MarkerType.PartSpawn && m.Part is not null).ToList();
        foreach (var group in markers.GroupBy(m => m.Group))
        {
            var list = group.OrderBy(_ => rand.Next()).ToList();
            float total = list.Count > 1 ? list.Sum(m => m.PartChance) : 1;
            float cumulative = 0;
            foreach (var m in list)
            {
                cumulative += m.PartChance / total;
                if (rand.NextSingle() > cumulative) continue;
                if (m.Tags.Length > 0 && (district is null
                    || !m.Tags.Split(',', StringSplitOptions.TrimEntries).Contains(district, StringComparer.OrdinalIgnoreCase))) continue;
                if (library.Find(m.Part!) is not { } part) continue;
                // The game turns part markers the other way round from POI lots.
                int markerTurns = m.Rotations switch { 1 => 3, 3 => 1, var r => r };
                int rotation = (spot.Rotation + part.RotationToFaceNorth + markerTurns) & 3;
                var (sx, sz) = part.Footprint(rotation);
                int x = spot.X + m.Start.X, z = spot.Z + m.Start.Z;
                if (taken.Any(b => b.X0 < x + sx - 1 && b.X1 > x + 1 && b.Z0 < z + sz - 1 && b.Z1 > z + 1)) continue;
                taken.Add((x, z, x + sx, z + sz));
                result.Add(new PrefabInstance { Name = part.Name, Position = new Int3(x, spot.Y + m.Start.Y, z), Rotation = rotation });
                break;
            }
        }
        return result;
    }
}
