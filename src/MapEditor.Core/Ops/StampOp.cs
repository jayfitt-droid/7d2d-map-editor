using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <param name="X">Centre, grid column (metres from the west edge).</param>
/// <param name="Z">Centre, grid row (metres from the south edge).</param>
/// <param name="SizeMetres">Width of the stamp on the ground.</param>
/// <param name="RotationDegrees">Clockwise rotation as seen on the north-up map.</param>
/// <param name="Strength">Multiplies the stamp's alpha (0..1); the game uses 1 for the first stamp of a cluster.</param>
/// <param name="Additive">Add the stamp's height instead of blending toward it.</param>
/// <param name="HeightScale">Multiplies the stamp's height values.</param>
/// <param name="HeightOffset">Added to the stamp's height values (metres).</param>
/// <param name="ApplyWater">
/// Also fill water: the stamp's water mask if it has one (lakes), otherwise its alpha (as the game does for
/// water_border). Water only stays where the terrain ends up at or below the surface.
/// </param>
public sealed record StampPlacement(
    float X,
    float Z,
    float SizeMetres,
    float RotationDegrees,
    float Strength = 1,
    bool Additive = false,
    float HeightScale = 1,
    float HeightOffset = 0,
    bool ApplyWater = false,
    byte WaterLevel = StampOp.DefaultWaterLevel);

/// <summary>Draws a game stamp into the heightmap the way RWG does (StampManager.DrawStamp).</summary>
public static class StampOp
{
    /// <summary>RWG's water surface height (WorldBuilder.WaterHeight).</summary>
    public const byte DefaultWaterLevel = 30;

    public static GridRect Apply(World world, StampImage stamp, StampPlacement p, EditSession session)
    {
        var height = world.Height;
        float scale = p.SizeMetres / stamp.Size;
        float half = p.SizeMetres / 2;
        // The rotated square fits inside a circle of radius half*sqrt(2).
        var rect = GridRect.AroundCircle(p.X, p.Z, half * 1.4143f).Clamp(height.Width, height.Height);
        if (rect.IsEmpty) return GridRect.Empty;

        bool water = p.ApplyWater;
        Grid<byte>? waterGrid = null;
        if (water)
        {
            waterGrid = world.Water ??= new Grid<byte>(world.Size, world.Size);
            session.Touch(waterGrid, rect);
        }
        session.Touch(height, rect);

        // Map rotation is clockwise on a north-up view; grid z points north, so that is a negative angle in grid space.
        float angle = -p.RotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float centre = stamp.Size / 2f;

        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var row = height.Row(z);
            float dz = z - p.Z;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                float dx = x - p.X;
                // Inverse-rotate the destination cell into stamp pixel space.
                float sx = (dx * cos + dz * sin) / scale + centre;
                float sy = (-dx * sin + dz * cos) / scale + centre;
                float alpha = stamp.SampleAlpha(sx, sy) * p.Strength;
                if (alpha >= 1e-5f)
                {
                    float target = stamp.SampleHeight(sx, sy) * p.HeightScale + p.HeightOffset;
                    float h = row[x] / 257f;
                    h = p.Additive ? h + target * alpha : h + (target - h) * Math.Min(alpha, 1f);
                    row[x] = TerrainBrush.ToRaw(h);
                }
                if (water && waterGrid![x, z] == 0 && (stamp.HasWater ? stamp.SampleWater(sx, sy) : stamp.SampleAlpha(sx, sy)) >= 1e-5f)
                    waterGrid[x, z] = p.WaterLevel;
            }
        }

        if (water) FinalizeWater(height, waterGrid!, rect);
        return rect;
    }

    /// <summary>Keeps water only where the terrain is at or below its surface (WorldBuilder.FinalizeWater).</summary>
    public static void FinalizeWater(Grid<ushort> height, Grid<byte> water, GridRect rect)
    {
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var h = height.Row(z);
            var w = water.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
                if (w[x] > 0 && h[x] / 257f - 0.5f > w[x]) w[x] = 0;
        }
    }
}
