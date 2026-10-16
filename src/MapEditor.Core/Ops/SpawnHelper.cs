using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

public static class SpawnHelper
{
    /// <summary>
    /// The game has no fallback when spawnpoints.xml is empty (SpawnPointList returns SpawnPosition.Undef),
    /// so a world without spawns gets one on the dry ground nearest the centre. Returns the spawn added, or null.
    /// </summary>
    public static SpawnPoint? EnsureOne(World world)
    {
        if (world.Spawns.Count > 0) return null;
        int size = world.Size, centre = size / 2;
        // Walk outward in 16 m rings until the ground is above any water surface.
        for (int r = 0; r < centre; r += 16)
        {
            for (int i = 0; i < Math.Max(1, r / 2); i++)
            {
                double angle = 2 * Math.PI * i / Math.Max(1, r / 2);
                int x = centre + (int)(r * Math.Cos(angle)), z = centre + (int)(r * Math.Sin(angle));
                if (!world.Height.Contains(x, z)) continue;
                float h = World.HeightToMetres(world.Height[x, z]);
                byte water = world.Water?[x, z] ?? 0;
                if (water > 0 && h <= water + 0.5f) continue;
                var spawn = new SpawnPoint { X = world.GridToWorld(x), Y = MathF.Ceiling(h) + 1, Z = world.GridToWorld(z) };
                world.Spawns.Add(spawn);
                return spawn;
            }
        }
        return null;
    }
}
