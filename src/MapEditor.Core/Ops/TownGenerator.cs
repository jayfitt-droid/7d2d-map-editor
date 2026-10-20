using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>What to build: the township type from rwgmixer.xml and the seed for the random choices.</summary>
public sealed record TownOptions(TownshipType Township, int Seed)
{
    /// <summary>Clear prefabs in the way; otherwise cells holding prefabs are left out of the town.</summary>
    public bool ReplacePrefabs { get; init; } = true;

    /// <summary>Run a road from each town exit to the nearest road (within <see cref="TownGenerator.RoadSearch"/>).</summary>
    public bool ConnectRoads { get; init; } = true;

    /// <summary>Add the street furniture, cars and props the prefabs' part markers call for.</summary>
    public bool AddParts { get; init; } = true;

    /// <summary>
    /// How many traders the town gets, as long as enough lots are big enough. RWG leaves this to chance, so small
    /// towns often get none and big ones several; here ordinary lots never get a trader, and this many lots are kept
    /// for one: the first nearest the town's middle, the rest spread apart.
    /// </summary>
    public int Traders { get; init; } = 1;

    /// <summary>Whose surface and width a link road takes: the road it joins, or the town street it leaves from.</summary>
    public LinkRoadStyle LinkStyle { get; init; } = LinkRoadStyle.JoinedRoad;
}

public enum LinkRoadStyle
{
    /// <summary>Match the road the link runs to.</summary>
    JoinedRoad,
    /// <summary>Match the street where it leaves the town's tile.</summary>
    Tile,
}

/// <summary>
/// One 150 m town cell. Cell (Cx, Cz) covers grid metres Origin + CxÂ·150 to Origin + CxÂ·150 + 150 east and the
/// same north. The origin is the corner of the town's selection, so a town lines up with what was drawn rather than
/// with RWG's world-wide grid. Exits are bits: 0 north, 1 east, 2 south, 3 west.
/// </summary>
public sealed class TownTile(int cx, int cz, int originX = 0, int originZ = 0)
{
    public int Cx { get; } = cx;
    public int Cz { get; } = cz;
    public int OriginX { get; } = originX;
    public int OriginZ { get; } = originZ;
    public DistrictType? District { get; set; }
    public int Exits { get; set; }
    public PrefabInstance? Prefab { get; set; }

    public bool IsGateway => District?.Kind == DistrictKind.Gateway;
    public int X0 => OriginX + Cx * PrefabPlacer.StreetTileSize;
    public int Z0 => OriginZ + Cz * PrefabPlacer.StreetTileSize;
    public GridRect Cells => new(X0, Z0, X0 + PrefabPlacer.StreetTileSize, Z0 + PrefabPlacer.StreetTileSize);
    public Vector2 Centre => new(X0 + 75, Z0 + 75);

    /// <summary>The middle of the tile's edge in direction <paramref name="dir"/>, as StreetTile.HighwayExitFromDir has it.</summary>
    public Vector2 ExitPoint(int dir) => dir switch
    {
        0 => new(X0 + 75, Z0 + 149),
        1 => new(X0 + 149, Z0 + 75),
        2 => new(X0 + 75, Z0),
        _ => new(X0, Z0 + 75),
    };

    public override string ToString() => $"{District?.Name ?? "none"} ({Cx}, {Cz}) exits {Exits}";
}

/// <summary>A planned town, ready to preview, reroll or apply as one undo step.</summary>
public sealed class TownPlan
{
    public required TownshipType Township { get; init; }
    public int Seed { get; init; }

    /// <summary>Grid metres of the south-west corner of cell (0, 0): the selection's corner.</summary>
    public int OriginX { get; init; }
    public int OriginZ { get; init; }

    /// <summary>The tiles in RWG's street order: outskirts first, then the core, then gateways.</summary>
    public List<TownTile> Tiles { get; } = [];

    /// <summary>The dtm.raw ground height the whole town is flattened to.</summary>
    public int Ground { get; set; }

    /// <summary>Street tiles, POIs and parts to add.</summary>
    public List<PrefabInstance> Prefabs { get; } = [];

    /// <summary>Prefabs in the way, which the town replaces.</summary>
    public List<PrefabInstance> Removed { get; } = [];

    /// <summary>Roads from the town's exits to the road network, graded.</summary>
    public List<RoadPath> Roads { get; } = [];

    public List<string> Warnings { get; } = [];

    public int PoiCount { get; set; }
    public int PartCount { get; set; }
    public int TraderCount { get; set; }

    /// <summary>How far the ground is blended back around each tile (StreetTile.SmoothTownshipTerrain).</summary>
    public int Blend => Tiles.Count <= 2 ? PrefabPlacer.StreetTileBlend : 110;

    public GridRect Bounds => Tiles.Aggregate(GridRect.Empty, (r, t) => r.Union(t.Cells));
}

/// <summary>
/// Builds a town in a selected area the way RWG plans one (TownPlanner, DistrictPlanner, StreetTile): 150 m street
/// tiles, districts grown outwards from the centre, gateways at the edge, streets joined between tiles, a street
/// tile prefab per cell and a POI in every lot group, all on one flattened level.
/// </summary>
public static class TownGenerator
{
    public const int Tile = PrefabPlacer.StreetTileSize;

    /// <summary>How far to look for a road to join the town's exits to, in metres.</summary>
    public const int RoadSearch = 2000;

    private static readonly (int X, int Z)[] Dir4 = [(0, 1), (1, 0), (0, -1), (-1, 0)];
    private static readonly (int X, int Z)[] Diagonals = [(1, 1), (1, -1), (-1, -1), (-1, 1)];

    /// <summary>StreetTileShared.RoadShapes and the exits of each shape before turning.</summary>
    public static readonly string[] ShapeNames = ["straight", "t", "intersection", "cap", "corner"];
    private static readonly int[] ShapeExits = [5, 7, 15, 4, 6];

    /// <summary>Marker.MarkerSizes: the standard lot sizes. Index 0 is never matched, as in the game.</summary>
    private static readonly int[] MarkerSizes = [1, 25, 42, 60, 100];

    /// <summary>The exits of <paramref name="shape"/> after <paramref name="rotations"/> clockwise quarter turns.</summary>
    public static int ShapeExitsAt(int shape, int rotations)
    {
        int exits = ShapeExits[shape];
        for (int i = 0; i < (rotations & 3); i++) exits = ((exits << 1) & 15) | (exits >> 3);
        return exits;
    }

    /// <summary>The road shape and turn that has exactly these exits (StreetTile.SetRoadShape); (-1, 0) for none.</summary>
    public static (int Shape, int Rotations) ShapeFor(int exits)
    {
        for (int shape = 0; shape < ShapeExits.Length; shape++)
            for (int r = 0; r < 4; r++)
                if (ShapeExitsAt(shape, r) == exits) return (shape, r);
        return (-1, 0);
    }

    /// <summary>The prefab rotation for a street tile prefab turned <paramref name="rotations"/> times (spawnStreetTile).</summary>
    public static int TileRotation(PrefabInfo tile, int rotations) => ((rotations + tile.RotationToFaceNorth) & 3) switch
    {
        1 => 3,
        3 => 1,
        var r => r,
    };

    /// <summary>The biome tag RWG uses for a biome name ("pine_forest" is "forest").</summary>
    public static string BiomeTag(string biome) => biome.ToLowerInvariant() switch
    {
        "pine_forest" => "forest",
        "burnt_forest" => "burntforest",
        var b => b,
    };

    /// <summary>The biome tag at grid point (x, z), or null if the world has no biome map.</summary>
    public static string? BiomeTagAt(World world, BiomeTable biomes, int x, int z)
    {
        if (world.Biomes is not { } b) return null;
        int s = Math.Max(1, world.Size / b.Indices.Width);
        x = Math.Clamp(x, 0, world.Size - 1);
        z = Math.Clamp(z, 0, world.Size - 1);
        return biomes.FindByColor(b.Palette[b.Indices[x / s, z / s]]) is { } def ? BiomeTag(def.Name) : null;
    }

    /// <summary>
    /// The whole 150 m cells in <paramref name="selection"/> (grid metres), counted from its south-west corner:
    /// cell (0, 0) starts at (X0, Z0).
    /// </summary>
    public static List<(int Cx, int Cz)> CellsIn(World world, GridRect selection)
    {
        selection = selection.Clamp(world.Size, world.Size);
        var cells = new List<(int, int)>();
        for (int cz = 0; selection.Z0 + (cz + 1) * Tile <= selection.Z1; cz++)
            for (int cx = 0; selection.X0 + (cx + 1) * Tile <= selection.X1; cx++)
                cells.Add((cx, cz));
        return cells;
    }

    /// <summary>
    /// The township to offer first for a selection: one RWG allows in the biome, whose tile range holds the
    /// number of cells, else the closest range.
    /// </summary>
    public static TownshipType? DefaultTownship(RwgMixer mixer, string? biomeTag, int cells)
    {
        var options = mixer.BuildableTownships.ToList();
        var inBiome = options.Where(t => t.Biomes.Count > 0 && t.AllowsBiome(biomeTag)).ToList();
        if (inBiome.Count == 0) inBiome = options.Where(t => t.AllowsBiome(biomeTag)).ToList();
        if (inBiome.Count == 0) inBiome = options;
        int Miss(TownshipType t) => cells < t.Tiles.Min ? t.Tiles.Min - cells : cells > t.Tiles.Max ? cells - t.Tiles.Max : 0;
        return inBiome.OrderBy(Miss).ThenBy(t => t.IsRoadside ? 1 : 0).FirstOrDefault();
    }

    // ---- planning ------------------------------------------------------------

    /// <summary>Plans a town in <paramref name="selection"/> (grid metres). Nothing in the world changes until <see cref="Apply"/>.</summary>
    public static TownPlan Plan(World world, PrefabLibrary library, RwgMixer mixer, BiomeTable biomes, GridRect selection,
        TownOptions options)
    {
        var township = options.Township;
        var clamped = selection.Clamp(world.Size, world.Size);
        var plan = new TownPlan { Township = township, Seed = options.Seed, OriginX = clamped.X0, OriginZ = clamped.Z0 };

        // Cells a town may use: wholly selected, dry, and free of prefabs unless they are to be replaced.
        var footprints = world.Prefabs.Select(p => (Prefab: p, Rect: PrefabPlacer.Footprint(world, p, library))).ToList();
        var valid = new HashSet<(int, int)>();
        int wet = 0, taken = 0;
        foreach (var cell in CellsIn(world, selection))
        {
            int x0 = plan.OriginX + cell.Cx * Tile, z0 = plan.OriginZ + cell.Cz * Tile;
            var rect = new GridRect(x0, z0, x0 + Tile, z0 + Tile);
            if (IsWet(world, rect)) { wet++; continue; }
            if (!options.ReplacePrefabs && footprints.Any(f => Overlaps(f.Rect, rect))) { taken++; continue; }
            valid.Add(cell);
        }
        if (wet > 0) plan.Warnings.Add($"{wet} cells left out for water");
        if (taken > 0) plan.Warnings.Add($"{taken} cells left out for prefabs");
        if (valid.Count == 0)
        {
            plan.Warnings.Add("no 150 m cell inside the selection can take a street tile");
            return plan;
        }

        var town = LayOut(plan, mixer, valid, township, options.Seed, world);
        if (plan.Tiles.Count == 0) return plan;

        if (plan.Ground == 0) plan.Ground = TownGround(world, CentreTile(plan.Tiles));

        if (options.ReplacePrefabs)
        {
            var area = plan.Tiles.Select(t => t.Cells).ToList();
            plan.Removed.AddRange(footprints.Where(f => area.Any(a => Overlaps(a, f.Rect))).Select(f => f.Prefab));
        }
        // Link roads keep clear of every prefab that stays, other towns' street tiles included.
        var removed = plan.Removed.ToHashSet();
        var roads = options.ConnectRoads ? RoadPixels(world, plan, town) : [];
        OpenGateways(plan, town, roads);
        // Tiles are picked before the link roads are routed, so a road can take the width of the street it leaves.
        new Filler(world, library, mixer, biomes, plan, options).Fill();
        if (options.ConnectRoads)
            PlanLinks(world, library, plan, town, township, options, roads,
                footprints.Where(f => !removed.Contains(f.Prefab)).Select(f => f.Rect).ToList());
        return plan;
    }

    /// <summary>
    /// Picks the town's tiles, districts, gateways and exits. Exposed with the cell set as input so tests can
    /// lay out a known town.
    /// </summary>
    public static Dictionary<(int, int), TownTile> LayOut(TownPlan plan, RwgMixer mixer, HashSet<(int, int)> valid,
        TownshipType township, int seed, World? world = null)
    {
        var town = new Dictionary<(int, int), TownTile>();
        void Add(TownTile t)
        {
            town[(t.Cx, t.Cz)] = t;
            plan.Tiles.Add(t);
        }
        var order = valid.OrderBy(c => c.Item2).ThenBy(c => c.Item1).ToList();
        mixer.Districts.TryGetValue("gateway", out var gateway);

        if (township.IsRoadside)
        {
            // Roadside stops are gateway tiles only (DistrictPlanner.GenerateGateway).
            if (gateway is null)
            {
                plan.Warnings.Add("rwgmixer.xml has no gateway district");
                return town;
            }
            foreach (var c in order) Add(new TownTile(c.Item1, c.Item2, plan.OriginX, plan.OriginZ) { District = gateway });
            SetExitsToTown(plan, town);
            return town;
        }

        // RWG grows outskirts round the town and puts gateways beyond them. Inside a selection that means keeping
        // the outer ring of cells for gateways and the next for outskirts; small selections give up the rings.
        var core = order;
        for (int ring = township.SpawnGateway ? 2 : 1; ring >= 1; ring--)
        {
            var inner = order.Where(c => Within(valid, c, ring)).ToList();
            if (inner.Count == 0) continue;
            core = inner;
            break;
        }
        var coreSet = core.ToHashSet();

        // Outskirts (TownPlanner.Grow): neighbours of the core, thinned to the township's share.
        var outskirts = new List<(int, int)>();
        if (township.OutskirtPercent > 0 && township.OutskirtDistrict is { } outName
            && mixer.Districts.TryGetValue(outName, out var outDistrict))
        {
            var dirs = township.OutskirtPercent < 1 ? Dir4 : Dir4.Concat(Diagonals).ToArray();
            foreach (var c in core)
                foreach (var d in dirs)
                {
                    var n = (c.Item1 + d.X, c.Item2 + d.Z);
                    if (valid.Contains(n) && !coreSet.Contains(n) && !outskirts.Contains(n)) outskirts.Add(n);
                }
            if (township.OutskirtPercent < 1)
            {
                float drop = 1 - township.OutskirtPercent, sum = 0;
                for (int i = outskirts.Count - 1; i >= 0; i--)
                {
                    sum += drop;
                    if (sum < 1) continue;
                    sum--;
                    outskirts.RemoveAt(i);
                }
            }
            foreach (var c in outskirts) Add(new TownTile(c.Item1, c.Item2, plan.OriginX, plan.OriginZ) { District = outDistrict });
        }
        foreach (var c in core) Add(new TownTile(c.Item1, c.Item2, plan.OriginX, plan.OriginZ));

        var districts = mixer.Districts.Values
            .Where(d => d.Weight > 0 && d.Townships.Contains(township.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (districts.Count == 0)
        {
            plan.Warnings.Add($"rwgmixer.xml has no districts for {township.Name}");
            plan.Tiles.Clear();
            town.Clear();
            return town;
        }
        var centre = CentreTile(plan.Tiles);
        if (world is not null) plan.Ground = TownGround(world, centre);
        foreach (var dropped in AssignDistricts(plan.Tiles, districts, centre))
        {
            town.Remove((dropped.Cx, dropped.Cz));
            plan.Tiles.Remove(dropped);
        }

        if (township.SpawnGateway && gateway is not null)
        {
            for (int pass = 0; pass < (township.IsBig ? 2 : 1); pass++)
                for (int dir = 0; dir < 4; dir++)
                    AddGateway(plan, town, valid, gateway, dir);
        }

        SetExitsToTown(plan, town);
        var rand = new Random(seed);
        foreach (var t in plan.Tiles.ToList())
        {
            if (t.IsGateway) continue;
            foreach (int dir in Shuffled([0, 1, 2, 3], rand))
            {
                if (Neighbour(town, t, dir) is not { District: { } nd } n || n.IsGateway || nd.Name == t.District!.Name) continue;
                int limit = t.District!.Kind == DistrictKind.Downtown || nd.Kind == DistrictKind.Downtown ? 2 : 1;
                if (CountDistrictExitsBetween(town, t, n) > limit) RemoveExit(town, t, dir);
            }
        }
        int capped = 0;
        foreach (var t in plan.Tiles)
        {
            if (t.IsGateway) continue;
            int bit = 1 << (int)t.District!.Kind;
            if ((capped & bit) == 0 && ChangeLToCap(town, t, rand)) capped |= bit;
        }
        return town;
    }

    /// <summary>
    /// DistrictPlanner.GenerateDistricts: tiles without a district are taken nearest the centre first, each
    /// district taking its share and growing from tiles it already has. Returns the tiles left without one.
    /// </summary>
    public static List<TownTile> AssignDistricts(IReadOnlyList<TownTile> streets, IReadOnlyList<DistrictType> districts, TownTile centreTile)
    {
        float total = districts.Sum(d => d.Weight);
        var avoid = districts.ToDictionary(d => d.Name, d => new HashSet<string>(d.AvoidedNeighbours, StringComparer.OrdinalIgnoreCase));
        foreach (var d in districts)
            foreach (var other in d.AvoidedNeighbours)
                if (avoid.TryGetValue(other, out var set)) set.Add(d.Name);

        var byCell = streets.ToDictionary(t => (t.Cx, t.Cz));
        var centre = centreTile.Centre + new Vector2(5, 0);
        var free = streets.Where(t => t.District is null).OrderBy(t => Vector2.Distance(centre, t.Centre)).ToList();
        var freeSet = free.ToHashSet();

        bool HasAvoidNeighbour(TownTile t, DistrictType d) => avoid[d.Name].Count > 0
            && Dir4.Any(o => byCell.GetValueOrDefault((t.Cx + o.X, t.Cz + o.Z)) is { District: { } nd } && avoid[d.Name].Contains(nd.Name));

        TownTile? FindFreeWithNeighbour(DistrictType d)
        {
            foreach (var offsets in new[] { Dir4, Diagonals })
                foreach (var t in free)
                {
                    if (!ReferenceEquals(t.District, d)) continue;
                    foreach (var o in offsets)
                        if (byCell.GetValueOrDefault((t.Cx + o.X, t.Cz + o.Z)) is { District: null } n && freeSet.Contains(n)
                            && !HasAvoidNeighbour(n, d))
                            return n;
                }
            return null;
        }

        var ordered = districts.OrderByDescending(d => d.SpawnOrder).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var d = ordered[i];
            int share = i == ordered.Count - 1 ? int.MaxValue : (int)MathF.Round(free.Count * (d.Weight / total));
            int count = 0;
            for (int k = 0; k < free.Count && count < share; k++)
            {
                var t = free[k];
                if (t.District is not null) continue;
                if (FindFreeWithNeighbour(d) is { } grown)
                {
                    t = grown;
                    k--;
                }
                else if (HasAvoidNeighbour(t, d)) continue;
                count++;
                t.District = d;
            }
        }
        return free.Where(t => t.District is null).ToList();
    }

    /// <summary>Township.CalcCenterStreetTile: the tile nearest the average tile position.</summary>
    public static TownTile CentreTile(IReadOnlyList<TownTile> tiles)
    {
        int sx = tiles.Sum(t => t.Cx), sz = tiles.Sum(t => t.Cz);
        int ax = sx / tiles.Count, az = sz / tiles.Count;
        TownTile best = tiles[0];
        int bestD = int.MaxValue;
        foreach (var t in tiles)
        {
            int d = (t.Cx - ax) * (t.Cx - ax) + (t.Cz - az) * (t.Cz - az);
            if (d < bestD) (best, bestD) = (t, d);
        }
        return best;
    }

    /// <summary>
    /// The town's dtm.raw ground: the average of nine samples 50 m apart round the centre tile's middle, rounded
    /// up, plus 3 (Township.CalcCenterStreetTile, without the snow and wasteland lifts).
    /// </summary>
    public static int TownGround(World world, TownTile centre)
    {
        float sum = 0;
        for (int dz = -50; dz <= 50; dz += 50)
            for (int dx = -50; dx <= 50; dx += 50)
            {
                int x = Math.Clamp((int)centre.Centre.X + dx, 0, world.Size - 1), z = Math.Clamp((int)centre.Centre.Y + dz, 0, world.Size - 1);
                sum += World.HeightToMetres(world.Height[x, z]);
            }
        return (int)MathF.Ceiling(sum / 9) + 3;
    }

    private static void AddGateway(TownPlan plan, Dictionary<(int, int), TownTile> town, HashSet<(int, int)> valid,
        DistrictType gateway, int dir)
    {
        foreach (var t in plan.Tiles)
        {
            if (t.IsGateway) continue;
            var c = (t.Cx + Dir4[dir].X, t.Cz + Dir4[dir].Z);
            if (town.ContainsKey(c) || !valid.Contains(c)) continue;
            int inTown = 0, usable = 0;
            foreach (var o in Dir4)
            {
                var n = (c.Item1 + o.X, c.Item2 + o.Z);
                if (!valid.Contains(n)) continue;
                usable++;
                if (town.ContainsKey(n) && ++inTown > 1) break;
            }
            if (inTown != 1 || usable < 2) continue;
            var g = new TownTile(c.Item1, c.Item2, plan.OriginX, plan.OriginZ) { District = gateway };
            town[c] = g;
            plan.Tiles.Add(g);
            return;
        }
    }

    /// <summary>True if every cell within <paramref name="ring"/> cells of <paramref name="c"/> (8-way) can be used.</summary>
    private static bool Within(HashSet<(int, int)> valid, (int X, int Z) c, int ring)
    {
        for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
                if (!valid.Contains((c.X + dx, c.Z + dz))) return false;
        return true;
    }

    private static TownTile? Neighbour(Dictionary<(int, int), TownTile> town, TownTile t, int dir) =>
        town.GetValueOrDefault((t.Cx + Dir4[dir].X, t.Cz + Dir4[dir].Z));

    private static void SetExitsToTown(TownPlan plan, Dictionary<(int, int), TownTile> town)
    {
        foreach (var t in plan.Tiles)
        {
            t.Exits = 0;
            for (int dir = 0; dir < 4; dir++)
                if (Neighbour(town, t, dir) is not null) t.Exits |= 1 << dir;
        }
    }

    private static void RemoveExit(Dictionary<(int, int), TownTile> town, TownTile t, int dir)
    {
        t.Exits &= ~(1 << dir);
        if (Neighbour(town, t, dir) is { } n) n.Exits &= ~(1 << ((dir + 2) & 3));
    }

    /// <summary>The tiles of the same district reachable from <paramref name="start"/> along streets.</summary>
    private static HashSet<TownTile> Connected(Dictionary<(int, int), TownTile> town, TownTile start)
    {
        var seen = new HashSet<TownTile> { start };
        var stack = new Stack<TownTile>([start]);
        while (stack.TryPop(out var t))
            for (int dir = 0; dir < 4; dir++)
                if ((t.Exits & (1 << dir)) != 0 && Neighbour(town, t, dir) is { } n && n.District?.Name == t.District?.Name && seen.Add(n))
                    stack.Push(n);
        return seen;
    }

    /// <summary>StreetTile.CountDistrictExitsBetween: streets joining the two tiles' connected districts.</summary>
    private static int CountDistrictExitsBetween(Dictionary<(int, int), TownTile> town, TownTile a, TownTile b)
    {
        var mine = Connected(town, a);
        var theirs = Connected(town, b);
        int count = 0;
        foreach (var t in mine)
            for (int dir = 0; dir < 4; dir++)
                if ((t.Exits & (1 << dir)) != 0 && Neighbour(town, t, dir) is { } n && theirs.Contains(n))
                    count++;
        return count;
    }

    /// <summary>
    /// StreetTile.ChangeLConnectionToCap: a corner joining two tiles of its own district becomes a dead end if
    /// the district stays connected without it.
    /// </summary>
    private static bool ChangeLToCap(Dictionary<(int, int), TownTile> town, TownTile t, Random rand)
    {
        if (t.Exits is not (3 or 6 or 12 or 9)) return false;
        int before = Connected(town, t).Count;
        var same = Enumerable.Range(0, 4)
            .Where(d => (t.Exits & (1 << d)) != 0 && Neighbour(town, t, d)?.District?.Name == t.District?.Name).ToList();
        if (same.Count != 2) return false;
        int dir = same[rand.Next(same.Count)];
        // The neighbour keeps its street to this tile, so the district is still whole only if a loop comes round.
        t.Exits &= ~(1 << dir);
        if (Connected(town, t).Count == before)
        {
            RemoveExit(town, t, dir);
            return true;
        }
        t.Exits |= 1 << dir;
        return false;
    }

    // ---- roads ---------------------------------------------------------------

    /// <summary>
    /// Opens the town to the outside: gateways get the exit facing away from town. A town without gateways opens
    /// the edge nearest a road.
    /// </summary>
    private static void OpenGateways(TownPlan plan, Dictionary<(int, int), TownTile> town, List<Vector2> roads)
    {
        foreach (var g in plan.Tiles.Where(t => t.IsGateway))
        {
            if (g.Exits == 0)
            {
                // A lone roadside tile runs its street along the axis that points at the nearest road.
                var near = Nearest(roads, g.Centre, null);
                bool eastWest = near is { } p && MathF.Abs(p.X - g.Centre.X) > MathF.Abs(p.Y - g.Centre.Y);
                g.Exits = eastWest ? 0b1010 : 0b0101;
            }
            else if (g.Exits is 1 or 2 or 4 or 8)
            {
                int dir = System.Numerics.BitOperations.TrailingZeroCount(g.Exits);
                g.Exits |= 1 << ((dir + 2) & 3);
            }
        }

        var open = OpenExits(town, plan.Tiles);
        if (open.Count == 0)
        {
            // No gateway: open the outer edge nearest a road, or the south edge of the first tile.
            (TownTile Tile, int Dir, float Distance)? best = null;
            foreach (var t in plan.Tiles)
                for (int dir = 0; dir < 4; dir++)
                {
                    if (Neighbour(town, t, dir) is not null) continue;
                    float d = Nearest(roads, t.ExitPoint(dir), Outward(dir)) is { } p ? Vector2.Distance(p, t.ExitPoint(dir)) : float.MaxValue;
                    if (best is null || d < best.Value.Distance) best = (t, dir, d);
                }
            if (best is { } b && (b.Distance < float.MaxValue || plan.Tiles.Any(t => t.Exits == 0)))
            {
                b.Tile.Exits |= 1 << b.Dir;
                open.Add((b.Tile, b.Dir));
            }
        }
        foreach (var t in plan.Tiles.Where(t => t.Exits == 0)) t.Exits = 0b0100;
    }

    /// <summary>
    /// Joins each open exit to the nearest road ahead of it. A link road takes the surface and width of the road
    /// it joins, or of the tile's street where it leaves the town (<see cref="TownOptions.LinkStyle"/>). The
    /// township's own style is only the fallback when neither can be measured.
    /// </summary>
    private static void PlanLinks(World world, PrefabLibrary library, TownPlan plan, Dictionary<(int, int), TownTile> town,
        TownshipType township, TownOptions options, List<Vector2> roads, List<GridRect> obstacles)
    {
        bool asphalt = township.Name.EndsWith("city", StringComparison.OrdinalIgnoreCase)
                       || township.Name.Equals("citybig", StringComparison.OrdinalIgnoreCase)
                       || township.Name.Equals("town", StringComparison.OrdinalIgnoreCase)
                       || township.Name.EndsWith("_town", StringComparison.OrdinalIgnoreCase);
        var fallback = asphalt ? (RoadType.Asphalt, 9f) : (RoadType.Gravel, 4.5f);
        var styles = new Dictionary<Vector2, (RoadType Type, float Radius)>();
        (RoadType Type, float Radius)? street = null;
        (RoadType Type, float Radius) StyleAt(Vector2 p)
        {
            if (street is { } s) return s;
            if (!styles.TryGetValue(p, out var style))
                styles[p] = style = MeasureRoad(world.Roads!, p) ?? fallback;
            return style;
        }
        var views = new Dictionary<(string, int), PrefabTopView?>();
        // The road and the ground it grades must miss prefabs that stay; past the lead it must miss this town too.
        static float Clearance((RoadType Type, float Radius) s) => s.Radius + new RoadPath([], s.Type, s.Radius).BlendDistance;
        var own = plan.Tiles.Select(t => t.Cells).ToList();
        bool Clear(Vector2 a, Vector2 b, bool missTown, float clearance) =>
            !obstacles.Any(r => SegmentHits(a, b, r, clearance)) && (!missTown || !own.Any(r => SegmentHits(a, b, r, clearance)));

        int missing = 0, blocked = 0;
        foreach (var (tile, dir) in OpenExits(town, plan.Tiles))
        {
            var start = tile.ExitPoint(dir);
            var outward = Outward(dir);
            street = options.LinkStyle == LinkRoadStyle.Tile ? TileStreet(library, tile, dir, views) : null;
            if (Nearest(roads, start, outward) is not { } near)
            {
                missing++;
                continue;
            }
            // Past the 40 m lead, head for the road nearest the lead's end, so the road turns once and runs
            // straight instead of hooking back towards the point nearest the exit. Nearer roads that would cross
            // a prefab or another town are passed over for the nearest clear one.
            var lead = start + outward * 40;
            Vector2[]? points = null;
            if (Vector2.Distance(start, near) <= 60 && Clear(start, near, missTown: false, Clearance(StyleAt(near))))
                points = [start, near];
            else if (NearestWhere(roads, lead, outward, start, p =>
                     {
                         float c = Clearance(StyleAt(p));
                         return Clear(start, lead, missTown: false, c) && Clear(lead, p, missTown: true, c);
                     }) is { } end)
                points = [start, lead, end];
            if (points is null)
            {
                blocked++;
                continue;
            }
            var (type, radius) = StyleAt(points[^1]);
            plan.Roads.Add(new RoadPath(points, type, radius));
        }
        if (missing > 0) plan.Warnings.Add($"no road within {RoadSearch / 1000} km of {missing} town exit{(missing == 1 ? "" : "s")}");
        if (blocked > 0)
            plan.Warnings.Add($"{blocked} town exit{(blocked == 1 ? "" : "s")} left unjoined: prefabs or another town are in the way");
    }

    /// <summary>
    /// The nearest road pixel ahead of <paramref name="aheadOf"/> that <paramref name="accept"/> takes, measured
    /// from <paramref name="from"/>. Pixels are tried nearest first.
    /// </summary>
    private static Vector2? NearestWhere(List<Vector2> roads, Vector2 from, Vector2 ahead, Vector2 aheadOf, Func<Vector2, bool> accept)
    {
        float limit = (float)RoadSearch * RoadSearch;
        var candidates = roads.Where(p => Vector2.Dot(p - aheadOf, ahead) > 0 && (p - from).LengthSquared() < limit)
            .OrderBy(p => (p - from).LengthSquared());
        // Neighbouring pixels share an answer, so skip ones close to a pixel already refused.
        var refused = new List<Vector2>();
        foreach (var p in candidates)
        {
            if (refused.Any(r => Vector2.DistanceSquared(r, p) < 16 * 16)) continue;
            if (accept(p)) return p;
            refused.Add(p);
            if (refused.Count > 400) break;
        }
        return null;
    }

    /// <summary>Whether segment aâ€“b passes within <paramref name="margin"/> of the rectangle.</summary>
    private static bool SegmentHits(Vector2 a, Vector2 b, GridRect rect, float margin)
    {
        // Liangâ€“Barsky clip against the rectangle grown by the margin.
        float x0 = rect.X0 - margin, z0 = rect.Z0 - margin, x1 = rect.X1 + margin, z1 = rect.Z1 + margin;
        var d = b - a;
        float t0 = 0, t1 = 1;
        foreach (var (p, q) in new[] { (-d.X, a.X - x0), (d.X, x1 - a.X), (-d.Y, a.Y - z0), (d.Y, z1 - a.Y) })
        {
            if (p == 0)
            {
                if (q < 0) return false;
                continue;
            }
            float r = q / p;
            if (p < 0) t0 = Math.Max(t0, r);
            else t1 = Math.Min(t1, r);
            if (t0 > t1) return false;
        }
        return true;
    }

    private static Vector2 Outward(int dir) => dir switch { 0 => new(0, 1), 1 => new(1, 0), 2 => new(0, -1), _ => new(-1, 0) };

    private static List<(TownTile Tile, int Dir)> OpenExits(Dictionary<(int, int), TownTile> town, IEnumerable<TownTile> tiles)
    {
        var open = new List<(TownTile, int)>();
        foreach (var t in tiles)
            for (int dir = 0; dir < 4; dir++)
                if ((t.Exits & (1 << dir)) != 0 && Neighbour(town, t, dir) is null) open.Add((t, dir));
        return open;
    }

    /// <summary>Road pixels (every other metre) within <see cref="RoadSearch"/> of the town, outside its tiles.</summary>
    private static List<Vector2> RoadPixels(World world, TownPlan plan, Dictionary<(int, int), TownTile> town)
    {
        var bounds = plan.Bounds;
        var found = new List<Vector2>();
        if (world.Roads is not { } roads || bounds.IsEmpty) return found;
        var area = new GridRect(bounds.X0 - RoadSearch, bounds.Z0 - RoadSearch, bounds.X1 + RoadSearch, bounds.Z1 + RoadSearch)
            .Clamp(world.Size, world.Size);
        for (int z = area.Z0; z < area.Z1; z += 2)
        {
            var row = roads.Row(z);
            for (int x = area.X0; x < area.X1; x += 2)
            {
                if (row[x] is not ((byte)RoadType.Asphalt or (byte)RoadType.Gravel)) continue;
                if (x >= plan.OriginX && z >= plan.OriginZ
                    && town.ContainsKey(((x - plan.OriginX) / Tile, (z - plan.OriginZ) / Tile))) continue;
                found.Add(new Vector2(x, z));
            }
        }
        return found;
    }

    /// <summary>
    /// The surface and half-width of the tile's street where it leaves through edge <paramref name="dir"/>, read
    /// from the tile prefab's blocks: the run of road blocks across the exit, a few metres in from the edge. Null
    /// when the tile has no prefab, its blocks can't be read, or no street crosses near the exit.
    /// </summary>
    private static (RoadType Type, float Radius)? TileStreet(PrefabLibrary library, TownTile tile, int dir,
        Dictionary<(string, int), PrefabTopView?> views)
    {
        if (tile.Prefab is not { } prefab || library.Find(prefab.Name) is not { } info) return null;
        var key = (prefab.Name, prefab.Rotation);
        if (!views.TryGetValue(key, out var view))
            views[key] = view = PrefabTopViewReader.Read(Path.Combine(info.Folder, info.Name + ".tts"), info.YOffset)?.Turned(prefab.Rotation);
        return view is null ? null : StreetAcross(view, dir);
    }

    /// <summary>
    /// The street crossing edge <paramref name="dir"/> of a top view (row 0 north): the median, over lines 1â€“5 m
    /// in from the edge, of the run of road pixels nearest the edge's middle.
    /// </summary>
    public static (RoadType Type, float Radius)? StreetAcross(PrefabTopView view, int dir)
    {
        bool northSouth = dir is 0 or 2;
        int length = northSouth ? view.Width : view.Height;
        int depth = northSouth ? view.Height : view.Width;
        uint Pixel(int inset, int i)
        {
            // i runs along the edge; inset counts in from it.
            int col, row;
            switch (dir)
            {
                case 0: col = i; row = inset; break;
                case 2: col = i; row = view.Height - 1 - inset; break;
                case 1: col = view.Width - 1 - inset; row = i; break;
                default: col = inset; row = i; break;
            }
            return view.Pixels[row * view.Width + col];
        }

        // Asphalt first: gravel verges and tracks beside a paved street are not part of it.
        int mid = length / 2;
        foreach (var surface in new[] { RoadType.Asphalt, RoadType.Gravel })
        {
            var widths = new List<int>();
            for (int inset = 1; inset <= 5 && inset < depth; inset++)
            {
                int found = -1;
                for (int off = 0; off <= length / 5 && found < 0; off++)
                    foreach (int i in new[] { mid - off, mid + off })
                        if (i >= 0 && i < length && PrefabTopViewReader.SurfaceOf(Pixel(inset, i)) == surface)
                        {
                            found = i;
                            break;
                        }
                if (found < 0) continue;
                int lo = found, hi = found;
                while (lo > 0 && PrefabTopViewReader.SurfaceOf(Pixel(inset, lo - 1)) == surface) lo--;
                while (hi < length - 1 && PrefabTopViewReader.SurfaceOf(Pixel(inset, hi + 1)) == surface) hi++;
                // A paved street's 1 m gravel shoulder counts, as RoadOp draws one on wide asphalt roads.
                if (surface == RoadType.Asphalt)
                {
                    if (lo > 0 && PrefabTopViewReader.SurfaceOf(Pixel(inset, lo - 1)) == RoadType.Gravel) lo--;
                    if (hi < length - 1 && PrefabTopViewReader.SurfaceOf(Pixel(inset, hi + 1)) == RoadType.Gravel) hi++;
                }
                widths.Add(hi - lo + 1);
            }
            if (widths.Count < 3) continue;
            widths.Sort();
            return (surface, Math.Clamp(widths[widths.Count / 2] / 2f, 1f, 12f));
        }
        return null;
    }

    /// <summary>
    /// The surface and half-width of the road painted at <paramref name="at"/>, measured from the road pixels:
    /// the road's direction is the long axis of the pixels around the point, and its width is the median run
    /// of road pixels straight across it. Asphalt anywhere across the road makes it asphalt, since highways
    /// have a gravel shoulder. Null when there is too little road there to measure.
    /// </summary>
    internal static (RoadType Type, float Radius)? MeasureRoad(Grid<byte> roads, Vector2 at)
    {
        const int Reach = 24;
        static bool IsRoad(byte b) => b is (byte)RoadType.Asphalt or (byte)RoadType.Gravel;
        int px = (int)at.X, pz = (int)at.Y;
        double n = 0, sx = 0, sz = 0, sxx = 0, szz = 0, sxz = 0;
        for (int z = Math.Max(0, pz - Reach); z <= Math.Min(roads.Height - 1, pz + Reach); z++)
        {
            var row = roads.Row(z);
            for (int x = Math.Max(0, px - Reach); x <= Math.Min(roads.Width - 1, px + Reach); x++)
            {
                if (!IsRoad(row[x]) || (x - px) * (x - px) + (z - pz) * (z - pz) > Reach * Reach) continue;
                n++;
                sx += x; sz += z;
                sxx += (double)x * x; szz += (double)z * z; sxz += (double)x * z;
            }
        }
        if (n < 8) return null;
        var centre = new Vector2((float)(sx / n) + 0.5f, (float)(sz / n) + 0.5f);
        double cxx = sxx / n - sx / n * (sx / n), czz = szz / n - sz / n * (sz / n), cxz = sxz / n - sx / n * (sz / n);
        float angle = 0.5f * (float)Math.Atan2(2 * cxz, cxx - czz);
        var along = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var across = new Vector2(-along.Y, along.X);

        bool asphalt = false;
        byte At(Vector2 p)
        {
            int x = (int)MathF.Floor(p.X), z = (int)MathF.Floor(p.Y);
            return x < 0 || z < 0 || x >= roads.Width || z >= roads.Height ? (byte)0 : roads[x, z];
        }
        var widths = new List<float>();
        for (int s = -8; s <= 8; s++)
        {
            var mid = centre + along * s;
            if (!IsRoad(At(mid))) continue;
            float width = 0.5f;
            foreach (var side in new[] { across, -across })
                for (float t = 0.5f; t <= Reach; t += 0.5f)
                {
                    byte b = At(mid + side * t);
                    if (!IsRoad(b)) break;
                    asphalt |= b == (byte)RoadType.Asphalt;
                    width += 0.5f;
                }
            asphalt |= At(mid) == (byte)RoadType.Asphalt;
            widths.Add(width);
        }
        if (widths.Count < 3) return null;
        widths.Sort();
        float radius = Math.Clamp(MathF.Round(widths[widths.Count / 2]) / 2, 1f, 12f);
        return (asphalt ? RoadType.Asphalt : RoadType.Gravel, radius);
    }

    /// <summary>The nearest road pixel within range, ahead of <paramref name="from"/> if a direction is given.</summary>
    private static Vector2? Nearest(List<Vector2> roads, Vector2 from, Vector2? ahead)
    {
        Vector2? best = null;
        float bestD = (float)RoadSearch * RoadSearch;
        foreach (var p in roads)
        {
            var d = p - from;
            if (ahead is { } a && Vector2.Dot(d, a) <= 0) continue;
            float ls = d.LengthSquared();
            if (ls < bestD) (best, bestD) = (p, ls);
        }
        return best;
    }

    // ---- prefabs -------------------------------------------------------------

    /// <summary>Fills a laid-out town with street tiles, POIs and parts (spawnStreetTile, SpawnMarkerPartsAndPrefabs).</summary>
    private sealed class Filler
    {
        private readonly World _world;
        private readonly PrefabLibrary _library;
        private readonly RwgMixer _mixer;
        private readonly BiomeTable _biomes;
        private readonly TownPlan _plan;
        private readonly TownOptions _options;
        private readonly List<Candidate> _candidates;
        private readonly Dictionary<string, int> _used = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Vector2>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Vector2>> _byTheme = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(Vector2 Centre, float Density)> _dense = [];
        private readonly int _distDiv;
        private int _unfilled;

        private sealed record Candidate(PrefabInfo Info, HashSet<string> Tags, HashSet<string> Themes, SpawnAdjust? Rule, bool RuleByName);

        public Filler(World world, PrefabLibrary library, RwgMixer mixer, BiomeTable biomes, TownPlan plan, TownOptions options)
        {
            _world = world;
            _library = library;
            _mixer = mixer;
            _biomes = biomes;
            _plan = plan;
            _options = options;
            _distDiv = world.Size > 4500 ? 1 : world.Size > 3500 ? 2 : world.Size > 2500 ? 3 : 4;

            var removed = plan.Removed.ToHashSet();
            foreach (var p in world.Prefabs)
                if (!removed.Contains(p)) Register(p, countDensity: false);

            // PrefabManagerData: tagged prefabs that are not tiles, parts or test pieces, in a seeded shuffle.
            string[] skip = ["streettile", "part", "navonly", "devonly", "testonly", "biomeonly"];
            var list = library.All
                .Where(p => p.Tags.Count > 0 && !p.IsStreetTile && !p.IsPart && !p.IsInternal && !skip.Any(p.HasTag))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    var tags = new HashSet<string>(p.Tags, StringComparer.OrdinalIgnoreCase);
                    var themes = new HashSet<string>(p.ThemeTags, StringComparer.OrdinalIgnoreCase);
                    var (rule, byName) = FirstRule(p, tags, themes);
                    return new Candidate(p, tags, themes, rule, byName);
                })
                .ToList();
            var shuffled = Shuffled(list, new Random(options.Seed ^ 0x5bd1e995));
            // Traders only go in the lots kept for them.
            _traders = shuffled.Where(c => c.Info.IsTrader).ToList();
            _candidates = shuffled.Where(c => !c.Info.IsTrader).ToList();
        }

        private readonly List<Candidate> _traders;

        private (SpawnAdjust?, bool) FirstRule(PrefabInfo p, HashSet<string> tags, HashSet<string> themes)
        {
            foreach (var rule in _mixer.SpawnAdjusts)
            {
                bool byName = rule.PartialName.Length > 0 && p.Name.Contains(rule.PartialName, StringComparison.OrdinalIgnoreCase);
                if (byName || rule.Tags.Any(tags.Contains) || rule.Tags.Any(themes.Contains)) return (rule, byName);
            }
            return (null, false);
        }

        /// <summary>The lots kept for traders: the tile's spot and the lot's start within it.</summary>
        private HashSet<(int X, int Z, Int3 Start)> _traderLots = [];

        private bool IsTraderLot(PrefabSpot spot, PrefabMarker lot, int depth) =>
            depth == 0 && _traderLots.Contains((spot.X, spot.Z, lot.Start));

        public void Fill()
        {
            var tileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _world.Prefabs.Except(_plan.Removed))
                if (p.Name.StartsWith("rwg_tile_", StringComparison.OrdinalIgnoreCase))
                    tileCounts[p.Name] = tileCounts.GetValueOrDefault(p.Name) + 1;

            // Every tile is picked before any lot is filled, so the trader's lot can be chosen across the whole town.
            var placed = new List<(TownTile Tile, PrefabInfo Info, PrefabSpot Spot)>();
            foreach (var t in _plan.Tiles)
            {
                var (shape, rotations) = ShapeFor(t.Exits);
                var rand = new Random(Hash(_options.Seed, t.Cx, t.Cz, 1));
                var info = PickTile("rwg_tile_" + t.District!.PrefabName + ShapeNames[shape], tileCounts, rand)
                           ?? PickTile("rwg_tile_" + ShapeNames[shape], tileCounts, rand);
                if (info is null)
                {
                    _plan.Warnings.Add($"no rwg_tile_{t.District.PrefabName}{ShapeNames[shape]} prefab");
                    continue;
                }
                int y = PrefabPlacer.PrefabY(info, _plan.Ground);
                if (y < 3) continue;
                int rotation = TileRotation(info, rotations);
                var spot = new PrefabSpot(_world.GridToWorld(t.X0), y, _world.GridToWorld(t.Z0), rotation);
                tileCounts[info.Name] = tileCounts.GetValueOrDefault(info.Name) + 1;
                t.Prefab = Add(info, spot);
                placed.Add((t, info, spot));
            }
            if (_options.Traders > 0) _traderLots = TraderLots(placed, _options.Traders);

            foreach (var (t, info, spot) in placed)
            {
                float budget = _mixer.TileMaxDensity.TryGetValue(info.Name, out float max) ? max : 62f;
                var taken = new List<(int X0, int Z0, int X1, int Z1)>();
                FillMarkers(t, info, spot, 0, budget, taken);
                if (t.IsGateway) AddHighwayTransitions(t, info, spot);
            }
            if (_unfilled > 0) _plan.Warnings.Add($"{_unfilled} lots found no POI");
        }

        /// <summary>
        /// Up to <paramref name="count"/> street-tile lots that some trader fits: the first nearest the town's middle,
        /// each next one the farthest from those already kept. At most one lot per group, since a group's lots are
        /// alternatives. Warns when there are fewer than asked for.
        /// </summary>
        private HashSet<(int X, int Z, Int3 Start)> TraderLots(List<(TownTile Tile, PrefabInfo Info, PrefabSpot Spot)> placed, int count)
        {
            var kept = new HashSet<(int, int, Int3)>();
            if (_traders.Count == 0)
            {
                _plan.Warnings.Add("no traders: the game folder has no trader prefabs");
                return kept;
            }
            var lots = new List<(int X, int Z, Int3 Start, string Group, Vector2 Centre)>();
            foreach (var (_, info, spot) in placed)
                foreach (var lot in info.RotatedMarkers(spot.Rotation).Where(m => m.Type == MarkerType.POISpawn))
                    if (_traders.Any(c => SizeValid(c.Info, 0, 0, lot.Size.X, lot.Size.Z)))
                        lots.Add((spot.X, spot.Z, lot.Start, lot.Group,
                            new Vector2(_world.WorldToGrid(spot.X) + lot.Start.X + lot.Size.X / 2f, _world.WorldToGrid(spot.Z) + lot.Start.Z + lot.Size.Z / 2f)));

            var bounds = _plan.Bounds;
            var middle = new Vector2((bounds.X0 + bounds.X1) / 2f, (bounds.Z0 + bounds.Z1) / 2f);
            var centres = new List<Vector2>();
            var groups = new HashSet<(int, int, string)>();
            while (kept.Count < count)
            {
                int best = -1;
                float bestScore = 0;
                for (int i = 0; i < lots.Count; i++)
                {
                    var l = lots[i];
                    if (groups.Contains((l.X, l.Z, l.Group))) continue;
                    // The first is the nearest the middle; later ones the farthest from every trader so far.
                    float score = centres.Count == 0 ? -Vector2.Distance(l.Centre, middle) : centres.Min(c => Vector2.Distance(c, l.Centre));
                    if (best < 0 || score > bestScore) (best, bestScore) = (i, score);
                }
                if (best < 0) break;
                var pick = lots[best];
                kept.Add((pick.X, pick.Z, pick.Start));
                groups.Add((pick.X, pick.Z, pick.Group));
                centres.Add(pick.Centre);
            }
            if (kept.Count < count)
                _plan.Warnings.Add(kept.Count == 0
                    ? "no traders: no lot in this town is big enough for one"
                    : $"only {kept.Count} of {count} traders: not enough lots are big enough");
            return kept;
        }

        /// <summary>The best scoring trader that fits a lot, ignoring the district, density and repeat rules.</summary>
        private PrefabInfo? ChooseTrader(int maxX, int maxZ, Vector2 centre)
        {
            string? biome = BiomeTagAt(_world, _biomes, (int)centre.X, (int)centre.Y);
            Candidate? best = null;
            float bestScore = float.MinValue;
            foreach (var c in _traders)
            {
                if (!SizeValid(c.Info, 0, 0, maxX, maxZ)) continue;
                float score = Score(c, biome);
                if (best is null || score > bestScore) (best, bestScore) = (c, score);
            }
            return best?.Info;
        }

        private PrefabInfo? PickTile(string prefix, Dictionary<string, int> counts, Random rand) =>
            _library.All
                .Where(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && (!_mixer.TileCounts.TryGetValue(p.Name, out var c) || counts.GetValueOrDefault(p.Name) < c.Max))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p, Key: (_mixer.TileCounts.TryGetValue(p.Name, out var c) ? c.Min : 0) + rand.NextDouble()))
                .ToList()
                .OrderByDescending(e => e.Key)
                .Select(e => e.p)
                .FirstOrDefault();

        private void FillMarkers(TownTile tile, PrefabInfo parent, PrefabSpot spot, int depth, float densityLeft,
            List<(int X0, int Z0, int X1, int Z1)> taken)
        {
            var markers = parent.RotatedMarkers(spot.Rotation);
            var district = tile.District!;
            var lots = markers.Where(m => m.Type == MarkerType.POISpawn).OrderByDescending(m => m.Size.X + m.Size.Y + m.Size.Z).ToList();
            if (depth < 5 && lots.Count > 0)
            {
                var rand = new Random(Hash(_options.Seed, spot.X, spot.Z, depth + 2));
                foreach (var group in lots.Select(m => m.Group).Distinct().ToList())
                {
                    // Lots in a group are alternatives and only one is filled, so the trader's lot goes first.
                    foreach (var lot in Shuffled(lots.Where(m => m.Group == group).ToList(), rand)
                                 .OrderBy(m => IsTraderLot(spot, m, depth) ? 0 : 1))
                    {
                        var (maxX, maxZ) = (lot.Size.X, lot.Size.Z);
                        var (minX, minZ) = (maxX, maxZ);
                        if (district.SpawnCustomSizePrefabs)
                        {
                            int index = maxX == maxZ ? Array.IndexOf(MarkerSizes, maxX, 1) : -1;
                            if (district.Kind != DistrictKind.Gateway && index > 0) (minX, minZ) = (MarkerSizes[index - 1] + 1, MarkerSizes[index - 1] + 1);
                            else (minX, minZ) = (maxX / 2, maxZ / 2);
                        }
                        // Lot centre in grid metres.
                        var centre = new Vector2(_world.WorldToGrid(spot.X) + lot.Start.X + maxX / 2, _world.WorldToGrid(spot.Z) + lot.Start.Z + maxZ / 2);
                        if (depth == 0)
                        {
                            float crowd = 0;
                            foreach (var (c, density) in _dense)
                                if (Vector2.Distance(c, centre) < 190) crowd += density >= 20 ? density * 1.3f : density - 6;
                            if (crowd > 0) densityLeft = Math.Max(6, densityLeft - crowd);
                        }
                        var tags = lot.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        var pick = (IsTraderLot(spot, lot, depth) ? ChooseTrader(maxX, maxZ, centre) : null)
                                   ?? Choose(district, tags, minX, minZ, maxX, maxZ, centre, densityLeft, 1)
                                   ?? Choose(district, tags, minX, minZ, maxX, maxZ, centre, densityLeft + 8, 0.3f)
                                   ?? Choose(district, tags, minX, minZ, maxX, maxZ, centre, 18, 0);
                        if (pick is null)
                        {
                            if (depth == 0) _unfilled++;
                            continue;
                        }
                        var poiSpot = PrefabPlacer.LotSpot(spot, lot, pick);
                        if (poiSpot.Y < 3) continue;
                        densityLeft -= pick.DensityScore;
                        Add(pick, poiSpot);
                        _plan.PoiCount++;
                        if (pick.IsTrader) _plan.TraderCount++;
                        FillMarkers(tile, pick, poiSpot, depth + 1, densityLeft, taken);
                        break;
                    }
                }
            }

            if (!_options.AddParts) return;
            // At the tile itself RWG matches part tags against the district's tags, deeper down against its name.
            string partDistrict = depth == 0 ? district.Tags.FirstOrDefault() ?? district.Name : district.Name;
            foreach (var part in PrefabPlacer.MarkerParts(_library, parent, spot, Hash(_options.Seed, spot.X, spot.Z, depth + 9), partDistrict, taken))
            {
                if (part.Name.Contains("highway_transition", StringComparison.OrdinalIgnoreCase)) continue;
                Register(part, countDensity: true);
                _plan.Prefabs.Add(part);
                _plan.PartCount++;
            }
        }

        /// <summary>Gateway tiles get a highway transition on each side with a street into town.</summary>
        private void AddHighwayTransitions(TownTile tile, PrefabInfo info, PrefabSpot spot)
        {
            var (sx, sz) = info.Footprint(spot.Rotation);
            foreach (var m in info.RotatedMarkers(spot.Rotation))
            {
                if (m.Part is null || !m.Part.Contains("highway_transition", StringComparison.OrdinalIgnoreCase)) continue;
                float dx = m.Start.X - sx / 2, dz = m.Start.Z - sz / 2;
                int dir = MathF.Abs(dx) > MathF.Abs(dz) ? (dx > 0 ? 1 : 3) : (dz > 0 ? 0 : 2);
                if ((tile.Exits & (1 << dir)) == 0 || _plan.Tiles.All(t => (t.Cx, t.Cz) != (tile.Cx + Dir4[dir].X, tile.Cz + Dir4[dir].Z))) continue;
                if (_library.Find(m.Part) is not { } part) continue;
                int turns = m.Rotations switch { 1 => 3, 3 => 1, var r => r };
                int rotation = (spot.Rotation + part.RotationToFaceNorth + turns) & 3;
                var instance = new PrefabInstance { Name = part.Name, Position = new Int3(spot.X + m.Start.X, spot.Y + m.Start.Y, spot.Z + m.Start.Z), Rotation = rotation };
                Register(instance, countDensity: true);
                _plan.Prefabs.Add(instance);
                _plan.PartCount++;
            }
        }

        /// <summary>PrefabManager.GetPrefabWithDistrict: the best scoring POI for a lot.</summary>
        private PrefabInfo? Choose(DistrictType district, string[] markerTags, int minX, int minZ, int maxX, int maxZ,
            Vector2 centre, float densityLeft, float distanceScale)
        {
            string? biome = BiomeTagAt(_world, _biomes, (int)centre.X, (int)centre.Y);
            Candidate? best = null;
            float bestScore = float.MinValue;
            foreach (var c in _candidates)
            {
                var p = c.Info;
                if (p.DensityScore > densityLeft) continue;
                if (district.Tags.Count > 0 && !district.Tags.All(c.Tags.Contains)) continue;
                if (markerTags.Length > 0 ? !markerTags.Any(c.Tags.Contains) : c.Tags.Contains("has")) continue;
                if (!SizeValid(p, minX, minZ, maxX, maxZ)) continue;
                int themeDistance = c.Themes.Contains("trader") ? p.ThemeRepeatDistance / _distDiv : p.ThemeRepeatDistance;
                if (!ThemeValid(c, centre, themeDistance)) continue;
                if (distanceScale > 0 && !NameValid(p, centre, (int)(p.DuplicateRepeatDistance * distanceScale))) continue;
                float score = Score(c, biome);
                if (score > bestScore) (best, bestScore) = (c, score);
            }
            return best?.Info;
        }

        /// <summary>PrefabManager.isSizeValid: fits within max and is at least min, either way round.</summary>
        private static bool SizeValid(PrefabInfo p, int minX, int minZ, int maxX, int maxZ)
        {
            int sx = p.Size.X, sz = p.Size.Z;
            if ((maxX | maxZ) != 0 && (sx > maxX || sz > maxZ) && (sz > maxX || sx > maxZ)) return false;
            if ((minX | minZ) == 0 || sx >= minX && sz >= minZ) return true;
            return sz >= minX && sx >= minZ;
        }

        private bool ThemeValid(Candidate c, Vector2 centre, int distance)
        {
            float limit = (float)distance * distance;
            foreach (var theme in c.Themes)
                if (_byTheme.TryGetValue(theme, out var list) && list.Any(p => Vector2.DistanceSquared(p, centre) < limit))
                    return false;
            return true;
        }

        private bool NameValid(PrefabInfo p, Vector2 centre, int distance)
        {
            float limit = (float)distance * distance;
            return !_byName.TryGetValue(p.Name, out var list) || !list.Any(c => Vector2.DistanceSquared(c, centre) < limit);
        }

        /// <summary>PrefabManager.getScoreForPrefab: spawn adjust rules, tier, and fewer points for repeats.</summary>
        private float Score(Candidate c, string? biome)
        {
            float bias = 1, weight = 1;
            int used = _used.GetValueOrDefault(c.Info.Name);
            if (c.Rule is { } rule)
            {
                if (c.RuleByName && rule.BiomeTags.Count > 0 && (biome is null || !rule.BiomeTags.Contains(biome, StringComparer.OrdinalIgnoreCase)))
                    return float.MinValue;
                weight = rule.Weight;
                bias += rule.Bias;
                if (used < rule.MinCount) bias += rule.MinCount - used;
                if (used > 0 && used >= rule.MaxCount) weight = 0;
            }
            float score = bias + c.Info.DifficultyTier / 5f;
            if (used > 0) score /= used + 1f;
            return score * weight;
        }

        private PrefabInstance Add(PrefabInfo info, PrefabSpot spot)
        {
            var instance = new PrefabInstance { Name = info.Name, Position = spot.Position, Rotation = spot.Rotation };
            Register(instance, countDensity: true);
            _plan.Prefabs.Add(instance);
            return instance;
        }

        /// <summary>Notes a prefab for the repeat, theme and density checks of later picks.</summary>
        private void Register(PrefabInstance p, bool countDensity)
        {
            _used[p.Name] = _used.GetValueOrDefault(p.Name) + 1;
            if (_library.Find(p.Name) is not { } info) return;
            var (sx, sz) = info.Footprint(p.Rotation);
            var centre = new Vector2(_world.WorldToGrid(p.Position.X) + sx / 2, _world.WorldToGrid(p.Position.Z) + sz / 2);
            if (!_byName.TryGetValue(p.Name, out var names)) _byName[p.Name] = names = [];
            names.Add(centre);
            foreach (var theme in info.ThemeTags)
            {
                if (!_byTheme.TryGetValue(theme, out var list)) _byTheme[theme] = list = [];
                list.Add(centre);
            }
            if (countDensity && info.DensityScore > 6) _dense.Add((centre, info.DensityScore));
        }
    }

    // ---- applying --------------------------------------------------------------

    /// <summary>
    /// Builds the planned town into the world as part of <paramref name="session"/>: removes what is in the way,
    /// flattens every tile to the town's level, adds the prefabs and grades the link roads.
    /// Returns the area changed.
    /// </summary>
    public static GridRect Apply(World world, PrefabLibrary library, TownPlan plan, EditSession session)
    {
        if (plan.Tiles.Count == 0) return GridRect.Empty;
        var bounds = plan.Bounds;
        var changed = new GridRect(bounds.X0 - plan.Blend - 1, bounds.Z0 - plan.Blend - 1, bounds.X1 + plan.Blend + 1, bounds.Z1 + plan.Blend + 1)
            .Clamp(world.Size, world.Size);

        var removed = plan.Removed.ToHashSet();
        var prefabArea = plan.Removed.Aggregate(bounds, (r, p) => r.Union(PrefabPlacer.Footprint(world, p, library)));
        session.TouchList(world.Prefabs, p => p.Clone(), prefabArea.Clamp(world.Size, world.Size));
        world.Prefabs.RemoveAll(removed.Contains);

        // RWG leaves splat3 empty under towns (the tiles carry their own streets), so old roads there go.
        if (world.Roads is { } roads)
            foreach (var t in plan.Tiles)
            {
                session.Touch(roads, t.Cells);
                for (int z = t.Z0; z < t.Z0 + Tile; z++) roads.Row(z).Slice(t.X0, Tile).Clear();
            }

        // The blend leaves roads that were already there as they are.
        foreach (var t in plan.Tiles) PrefabPlacer.Flatten(world, t.Cells, plan.Ground, plan.Blend, session);
        world.Prefabs.AddRange(plan.Prefabs);

        foreach (var road in plan.Roads) changed = changed.Union(RoadOp.Draw(world, road, session));
        return changed;
    }

    // ---- helpers ---------------------------------------------------------------

    private static bool Overlaps(GridRect a, GridRect b) => a.X0 < b.X1 && a.X1 > b.X0 && a.Z0 < b.Z1 && a.Z1 > b.Z0;

    /// <summary>RWG's water test for a street tile, every 3 m; only water standing above the ground counts.</summary>
    private static bool IsWet(World world, GridRect rect)
    {
        if (world.Water is not { } water) return false;
        for (int z = rect.Z0; z < rect.Z1; z += 3)
            for (int x = rect.X0; x < rect.X1; x += 3)
            {
                byte w = water[x, z];
                if (w > 0 && World.HeightToMetres(world.Height[x, z]) <= w + 0.5f) return true;
            }
        return false;
    }

    private static List<T> Shuffled<T>(List<T> list, Random rand)
    {
        var copy = list.ToList();
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    /// <summary>A seed for one choice, the same on every run (HashCode is randomised per process).</summary>
    private static int Hash(int seed, int a, int b, int c)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in (ReadOnlySpan<int>)[seed, a, b, c])
            {
                h = (h ^ (uint)v) * 16777619;
                h ^= h >> 15;
            }
            return (int)(h & int.MaxValue);
        }
    }
}
