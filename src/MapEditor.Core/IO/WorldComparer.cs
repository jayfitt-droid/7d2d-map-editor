using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Compares two worlds by what the game would read, for round-trip checks.</summary>
public static class WorldComparer
{
    public static List<string> Compare(World a, World b)
    {
        var diffs = new List<string>();
        if (a.Size != b.Size) { diffs.Add($"size {a.Size} vs {b.Size}"); return diffs; }

        CompareGrid("height", a.Height, b.Height, diffs);
        CompareGrid("roads", a.Roads, b.Roads, diffs);
        CompareGrid("water", a.Water, b.Water, diffs);
        CompareGrid("radiation", a.Radiation, b.Radiation, diffs);
        CompareBiomes(a.Biomes, b.Biomes, diffs);

        foreach (var key in a.PassthroughImages.Keys.Union(b.PassthroughImages.Keys))
        {
            a.PassthroughImages.TryGetValue(key, out var ia);
            b.PassthroughImages.TryGetValue(key, out var ib);
            if (ia is null || ib is null || ia.Width != ib.Width || !ia.Pixels.AsSpan().SequenceEqual(ib.Pixels))
                diffs.Add($"{key} raw pixels differ");
        }

        if (a.Prefabs.Count != b.Prefabs.Count) diffs.Add($"prefab count {a.Prefabs.Count} vs {b.Prefabs.Count}");
        else
            for (int i = 0; i < a.Prefabs.Count; i++)
            {
                var (p, q) = (a.Prefabs[i], b.Prefabs[i]);
                if (p.Name != q.Name || p.Position != q.Position || p.Rotation != q.Rotation || p.Type != q.Type)
                {
                    diffs.Add($"prefab #{i} {p.Name}@{p.Position} r{p.Rotation} vs {q.Name}@{q.Position} r{q.Rotation}");
                    break;
                }
            }

        if (a.Spawns.Count != b.Spawns.Count) diffs.Add($"spawn count {a.Spawns.Count} vs {b.Spawns.Count}");
        else
            for (int i = 0; i < a.Spawns.Count; i++)
            {
                var (s, t) = (a.Spawns[i], b.Spawns[i]);
                if (s.X != t.X || s.Y != t.Y || s.Z != t.Z || s.RotationY != t.RotationY)
                    diffs.Add($"spawn #{i} differs");
            }

        foreach (var key in a.ExtraFiles.Keys.Union(b.ExtraFiles.Keys))
        {
            a.ExtraFiles.TryGetValue(key, out var fa);
            b.ExtraFiles.TryGetValue(key, out var fb);
            if (fa is null || fb is null || !fa.AsSpan().SequenceEqual(fb)) diffs.Add($"extra file {key} differs");
        }

        return diffs;
    }

    private static void CompareGrid<T>(string name, Grid<T>? a, Grid<T>? b, List<string> diffs)
        where T : unmanaged, IEquatable<T>
    {
        if (a is null && b is null) return;
        if (a is null || b is null) { diffs.Add($"{name} present in only one world"); return; }
        if (a.Width != b.Width || a.Height != b.Height) { diffs.Add($"{name} size {a.Width}x{a.Height} vs {b.Width}x{b.Height}"); return; }
        int i = a.Data.AsSpan().CommonPrefixLength(b.Data);
        if (i < a.Data.Length) diffs.Add($"{name} differs first at x={i % a.Width} z={i / a.Width}");
    }

    private static void CompareBiomes(IndexedLayer? a, IndexedLayer? b, List<string> diffs)
    {
        if (a is null && b is null) return;
        if (a is null || b is null) { diffs.Add("biomes present in only one world"); return; }
        if (a.Indices.Width != b.Indices.Width || a.Indices.Height != b.Indices.Height) { diffs.Add("biomes size differs"); return; }
        for (int i = 0; i < a.Indices.Data.Length; i++)
        {
            if (a.Palette[a.Indices.Data[i]] != b.Palette[b.Indices.Data[i]])
            {
                diffs.Add($"biomes differ first at x={i % a.Indices.Width} z={i / a.Indices.Width}");
                return;
            }
        }
    }
}
