using MapEditor.Core.Edit;
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

    /// <summary>The height a spawn at grid cell (x, z) stands at: a metre above the rounded-up ground.</summary>
    public static float StandingHeight(World world, int x, int z)
    {
        x = Math.Clamp(x, 0, world.Size - 1);
        z = Math.Clamp(z, 0, world.Size - 1);
        return MathF.Ceiling(World.HeightToMetres(world.Height[x, z])) + 1;
    }

    /// <summary>The spawn nearest grid point (gx, gz) within <paramref name="radius"/> metres, or null.</summary>
    public static SpawnPoint? Near(World world, float gx, float gz, float radius)
    {
        float wx = gx - world.Size / 2f, wz = gz - world.Size / 2f;
        return world.Spawns
            .Select(s => (Spawn: s, D: MathF.Sqrt((s.X - wx) * (s.X - wx) + (s.Z - wz) * (s.Z - wz))))
            .Where(t => t.D <= radius)
            .OrderBy(t => t.D)
            .Select(t => t.Spawn)
            .FirstOrDefault();
    }

    public static SpawnPoint Add(World world, int gx, int gz, float rotationY, EditSession session)
    {
        var spawn = new SpawnPoint
        {
            X = world.GridToWorld(gx), Y = StandingHeight(world, gx, gz), Z = world.GridToWorld(gz), RotationY = rotationY,
        };
        session.TouchList(world.Spawns, s => s.Clone(), Around(gx, gz));
        world.Spawns.Add(spawn);
        return spawn;
    }

    public static void Move(World world, SpawnPoint spawn, int gx, int gz, EditSession session)
    {
        session.TouchList(world.Spawns, s => s.Clone(), Around(world.WorldToGrid((int)spawn.X), world.WorldToGrid((int)spawn.Z)));
        session.TouchList(world.Spawns, s => s.Clone(), Around(gx, gz));
        spawn.X = world.GridToWorld(gx);
        spawn.Z = world.GridToWorld(gz);
        spawn.Y = StandingHeight(world, gx, gz);
    }

    public static void Rotate(World world, SpawnPoint spawn, float degrees, EditSession session)
    {
        session.TouchList(world.Spawns, s => s.Clone(), Around(world.WorldToGrid((int)spawn.X), world.WorldToGrid((int)spawn.Z)));
        spawn.RotationY = ((spawn.RotationY + degrees) % 360 + 360) % 360;
    }

    public static void Delete(World world, SpawnPoint spawn, EditSession session)
    {
        session.TouchList(world.Spawns, s => s.Clone(), Around(world.WorldToGrid((int)spawn.X), world.WorldToGrid((int)spawn.Z)));
        world.Spawns.Remove(spawn);
    }

    private static GridRect Around(int gx, int gz) => new(gx - 2, gz - 2, gx + 3, gz + 3);
}
