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

    /// <summary>
    /// Sets the ground in <paramref name="cells"/> to <paramref name="height"/> metres and blends it back into
    /// the surrounding terrain with a smoothstep over <paramref name="blend"/> metres. Returns the cells changed.
    /// </summary>
    public static GridRect Flatten(World world, GridRect cells, int height, int blend, EditSession session)
    {
        var area = new GridRect(cells.X0 - blend - 1, cells.Z0 - blend - 1, cells.X1 + blend + 1, cells.Z1 + blend + 1)
            .Clamp(world.Size, world.Size);
        if (area.IsEmpty) return GridRect.Empty;
        session.Touch(world.Height, area);
        ushort flat = World.MetresToHeight(height);
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
                float current = World.HeightToMetres(world.Height[x, z]);
                world.Height[x, z] = World.MetresToHeight(current * s + height * (1 - s));
            }
        }
        return area;
    }

    /// <summary>
    /// The spot for <paramref name="info"/> centred on grid point (<paramref name="gx"/>, <paramref name="gz"/>).
    /// Street tiles snap to the 150 m grid and take the height of a neighbouring tile; POIs snap into a
    /// street-tile lot under the cursor when they fit (<paramref name="lot"/>), else sit on the median ground.
    /// </summary>
    public static PrefabSpot SpotAt(World world, PrefabLibrary library, PrefabInfo info, float gx, float gz, int rotation,
        out LotSnap? lot, IReadOnlyCollection<PrefabInstance>? ignore = null)
    {
        lot = null;
        rotation &= 3;
        if (info.IsStreetTile)
        {
            int tx = (int)MathF.Floor(gx / StreetTileSize) * StreetTileSize;
            int tz = (int)MathF.Floor(gz / StreetTileSize) * StreetTileSize;
            int x = world.GridToWorld(tx), z = world.GridToWorld(tz);
            int ground = NeighbourTileGround(world, library, info, x, z, rotation, ignore)
                         ?? MedianGround(world, Footprint(world, info, x, z, rotation));
            return new PrefabSpot(x, PrefabY(info, ground), z, rotation);
        }

        if (!info.IsPart && FindLot(world, library, info, gx, gz, ignore) is { } found)
        {
            lot = found;
            if (found.Fits) return found.Spot;
        }

        var (sx, sz) = info.Footprint(rotation);
        int wx = world.GridToWorld((int)MathF.Round(gx - sx / 2f)), wz = world.GridToWorld((int)MathF.Round(gz - sz / 2f));
        return new PrefabSpot(wx, PrefabY(info, MedianGround(world, Footprint(world, info, wx, wz, rotation))), wz, rotation);
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

                int rotation = (tile.Rotation + poi.RotationToFaceNorth + m.Rotations) & 3;
                var (sx, sz) = poi.Footprint(rotation);
                int x = lx, z = lz;
                switch (m.Rotations & 3)
                {
                    case 0: x += m.Size.X / 2 - sx / 2; z += m.Size.Z - sz; break;
                    case 1: z += m.Size.Z / 2 - sz / 2; break;
                    case 2: x += m.Size.X / 2 - sx / 2; break;
                    case 3: z += m.Size.Z / 2 - sz / 2; x += m.Size.X - sx; break;
                }
                bool fits = sx <= m.Size.X && sz <= m.Size.Z;
                var snap = new LotSnap(tile, m, lx, lz, fits, new PrefabSpot(x, tile.Position.Y + m.Start.Y + poi.YOffset, z, rotation));
                long area = (long)m.Size.X * m.Size.Z, bestArea = best is null ? 0 : (long)best.Lot.Size.X * best.Lot.Size.Z;
                if (best is null || fits && (!best.Fits || area < bestArea) || !fits && !best.Fits && area > bestArea) best = snap;
            }
            if (best is not null) return best;
        }
        return null;
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
        if (flatten && !snappedToLot && !info.IsPart)
            Flatten(world, cells, GroundUnder(info, spot.Y), info.IsStreetTile ? StreetTileBlend : PoiBlend, session);
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
        if (flatten && !snappedToLot && !info.IsPart)
            Flatten(world, after, GroundUnder(info, spot.Y), info.IsStreetTile ? StreetTileBlend : PoiBlend, session);
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
    /// </summary>
    public static List<PrefabInstance> MarkerParts(PrefabLibrary library, PrefabInfo prefab, PrefabSpot spot, int seed,
        string? district = null)
    {
        district ??= prefab.District;
        var rand = new Random(seed);
        var result = new List<PrefabInstance>();
        var taken = new List<(int X0, int Z0, int X1, int Z1)>();
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
