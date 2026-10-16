using MapEditor.Core.Edit;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>Result of a fill or drain: the changed area in metres and how many cells it covered.</summary>
public readonly record struct FillResult(GridRect Bounds, long Cells);

/// <summary>
/// Flood fills for splat4 water. A fill spreads from a point across all connected ground lower than
/// the water surface, so shorelines follow the terrain; a drain removes one connected body of water.
/// </summary>
public static class WaterFill
{
    /// <summary>
    /// Fills the basin around (x, z) up to <paramref name="surface"/> metres. Cells one step outside the
    /// basin get the surface too, so the shoreline has no gaps; the game only places water where the
    /// terrain is below the surface, so this is harmless above it.
    /// </summary>
    public static FillResult Fill(World world, int x, int z, byte surface, EditSession session)
    {
        if (surface == 0 || !world.Height.Contains(x, z)) return default;
        var height = world.Height;
        ushort limit = (ushort)(surface * 257);
        if (height[x, z] >= limit) return default;

        var mask = Flood(world.Size, x, z, i => height.Data[i] < limit, out var rect, out long cells);
        world.Water ??= new Grid<byte>(world.Size, world.Size);
        var water = world.Water;
        var bounds = new GridRect(rect.X0 - 1, rect.Z0 - 1, rect.X1 + 1, rect.Z1 + 1).Clamp(world.Size, world.Size);
        session.Touch(water, bounds);

        int size = world.Size;
        for (int zz = bounds.Z0; zz < bounds.Z1; zz++)
            for (int xx = bounds.X0; xx < bounds.X1; xx++)
            {
                int i = xx + zz * size;
                if (mask.Get(i))
                    water.Data[i] = surface;
                else if (water.Data[i] == 0 && (
                    (xx > 0 && mask.Get(i - 1)) || (xx < size - 1 && mask.Get(i + 1)) ||
                    (zz > 0 && mask.Get(i - size)) || (zz < size - 1 && mask.Get(i + size))))
                    water.Data[i] = surface;
            }
        return new FillResult(bounds, cells);
    }

    /// <summary>
    /// Removes the connected body of water under (x, z), including its shoreline cells. Only water with
    /// the same surface counts, so draining a lake that touches the sea leaves the sea alone.
    /// </summary>
    public static FillResult Drain(World world, int x, int z, EditSession session)
    {
        if (world.Water is not { } water || !water.Contains(x, z) || water[x, z] == 0) return default;
        byte surface = water[x, z];
        var mask = Flood(world.Size, x, z, i => water.Data[i] == surface, out var rect, out long cells);
        session.Touch(water, rect);
        for (int zz = rect.Z0; zz < rect.Z1; zz++)
            for (int xx = rect.X0; xx < rect.X1; xx++)
            {
                int i = xx + zz * world.Size;
                if (mask.Get(i)) water.Data[i] = 0;
            }
        return new FillResult(rect, cells);
    }

    /// <summary>4-connected scanline flood fill over a size x size grid. Returns the visited cells.</summary>
    private static BitMask Flood(int size, int x, int z, Func<int, bool> inside, out GridRect bounds, out long cells)
    {
        var mask = new BitMask(size * size);
        int minX = x, maxX = x, minZ = z, maxZ = z;
        cells = 0;
        var stack = new Stack<(int X, int Z)>();
        stack.Push((x, z));
        while (stack.TryPop(out var seed))
        {
            int row = seed.Z * size;
            if (mask.Get(seed.X + row) || !inside(seed.X + row)) continue;
            int left = seed.X, right = seed.X;
            while (left > 0 && !mask.Get(left - 1 + row) && inside(left - 1 + row)) left--;
            while (right < size - 1 && !mask.Get(right + 1 + row) && inside(right + 1 + row)) right++;
            for (int i = left; i <= right; i++) mask.Set(i + row);
            cells += right - left + 1;
            minX = Math.Min(minX, left);
            maxX = Math.Max(maxX, right);
            minZ = Math.Min(minZ, seed.Z);
            maxZ = Math.Max(maxZ, seed.Z);

            foreach (int nz in (ReadOnlySpan<int>)[seed.Z - 1, seed.Z + 1])
            {
                if ((uint)nz >= (uint)size) continue;
                int nrow = nz * size;
                // Push one seed per run of fillable cells in the neighbouring row.
                bool inRun = false;
                for (int i = left; i <= right; i++)
                {
                    bool fillable = !mask.Get(i + nrow) && inside(i + nrow);
                    if (fillable && !inRun) stack.Push((i, nz));
                    inRun = fillable;
                }
            }
        }
        bounds = new GridRect(minX, minZ, maxX + 1, maxZ + 1);
        return mask;
    }

    private sealed class BitMask(int length)
    {
        private readonly ulong[] _bits = new ulong[(length + 63) / 64];

        public bool Get(int i) => (_bits[i >> 6] & (1UL << (i & 63))) != 0;

        public void Set(int i) => _bits[i >> 6] |= 1UL << (i & 63);
    }
}
