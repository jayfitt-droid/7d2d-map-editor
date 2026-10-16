using MapEditor.Core.Edit;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

public enum BrushMode
{
    Raise,
    Lower,
    Flatten,
    Smooth,
    Noise,
}

public enum BrushShape
{
    Circle,
    /// <summary>A square whose half-width is the radius; it can be rotated.</summary>
    Square,
}

public enum BrushFalloff
{
    /// <summary>Smoothstep from full at the centre to nothing at the edge.</summary>
    Smooth,
    Linear,
    /// <summary>Full strength right up to the edge, for flat pads and sharp cuts.</summary>
    Hard,
}

/// <summary>The outline and edge of a brush tip, shared by the height and layer brushes.</summary>
public readonly record struct BrushTip(float Radius, BrushShape Shape = BrushShape.Circle, BrushFalloff Falloff = BrushFalloff.Smooth, float RotationDegrees = 0)
{
    /// <summary>Cells that can be inside the tip centred on (cx, cz).</summary>
    public GridRect Bounds(float cx, float cz) =>
        GridRect.AroundCircle(cx, cz, Shape == BrushShape.Square ? Radius * 1.41422f : Radius);

    /// <summary>Distance of offset (dx, dz) from the centre as a fraction of the radius; 1 or more is outside.</summary>
    public float Distance(float dx, float dz)
    {
        if (Shape == BrushShape.Circle) return MathF.Sqrt(dx * dx + dz * dz) / Radius;
        // RotationDegrees is clockwise on the north-up map, as for stamps; undo it to get tip-space (u, v).
        var (sin, cos) = MathF.SinCos(RotationDegrees * MathF.PI / 180f);
        float u = dx * cos - dz * sin, v = dx * sin + dz * cos;
        return Math.Max(MathF.Abs(u), MathF.Abs(v)) / Radius;
    }

    /// <summary>Weight 0..1 at a distance fraction from <see cref="Distance"/>.</summary>
    public float Weight(float distance)
    {
        if (distance >= 1) return 0;
        return Falloff switch
        {
            BrushFalloff.Hard => 1,
            BrushFalloff.Linear => 1 - distance,
            _ => TerrainBrush.Falloff(distance),
        };
    }
}

/// <param name="Radius">Brush radius in metres (half-width for a square).</param>
/// <param name="Strength">0..1. Raise/Lower/Noise move up to 4 m per dab at full strength; Flatten/Smooth blend that fraction per dab.</param>
/// <param name="TargetHeight">Flatten target in metres.</param>
/// <param name="NoiseScale">Noise feature size in metres.</param>
public sealed record BrushSettings(
    BrushMode Mode,
    float Radius,
    float Strength,
    float TargetHeight = 0,
    int NoiseSeed = 1,
    float NoiseScale = 64,
    BrushShape Shape = BrushShape.Circle,
    BrushFalloff Falloff = BrushFalloff.Smooth,
    float RotationDegrees = 0)
{
    public BrushTip Tip => new(Radius, Shape, Falloff, RotationDegrees);
}

/// <summary>Height brushes. A stroke is a series of dabs; each dab edits the cells under the brush tip.</summary>
public static class TerrainBrush
{
    private const float MaxStepMetres = 4f;
    private const float MaxHeight = 255f;

    /// <summary>Applies one dab centred on grid cell (cx, cz). Returns the cells changed.</summary>
    public static GridRect Dab(Grid<ushort> height, float cx, float cz, BrushSettings s, EditSession session, Noise? noise = null)
    {
        var tip = s.Tip;
        var rect = tip.Bounds(cx, cz).Clamp(height.Width, height.Height);
        if (rect.IsEmpty || s.Radius <= 0) return GridRect.Empty;
        session.Touch(height, rect);

        float[]? smoothed = s.Mode == BrushMode.Smooth ? BoxBlur(height, rect, Math.Max(1, (int)(s.Radius / 12))) : null;
        noise ??= s.Mode == BrushMode.Noise ? new Noise(s.NoiseSeed) : null;
        int w = rect.X1 - rect.X0;

        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var row = height.Row(z);
            float dz = z - cz;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                float weight = tip.Weight(tip.Distance(x - cx, dz));
                if (weight <= 0) continue;
                float h = row[x] / 257f;
                h = s.Mode switch
                {
                    BrushMode.Raise => h + MaxStepMetres * s.Strength * weight,
                    BrushMode.Lower => h - MaxStepMetres * s.Strength * weight,
                    BrushMode.Flatten => h + (s.TargetHeight - h) * Math.Min(1f, s.Strength * weight),
                    BrushMode.Smooth => h + (smoothed![(x - rect.X0) + (z - rect.Z0) * w] - h) * Math.Min(1f, s.Strength * weight),
                    BrushMode.Noise => h + MaxStepMetres * s.Strength * weight * noise!.Fbm(x / s.NoiseScale, z / s.NoiseScale),
                    _ => h,
                };
                row[x] = ToRaw(h);
            }
        }
        return rect;
    }

    /// <summary>Smooth 1 at the centre to 0 at the rim.</summary>
    public static float Falloff(float distanceFraction)
    {
        float t = Math.Clamp(1 - distanceFraction, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static ushort ToRaw(float metres) => (ushort)Math.Clamp(MathF.Round(metres * 257f), 0, MaxHeight * 257f);

    /// <summary>Box-blurred heights (metres) for the cells of <paramref name="rect"/>, using a summed-area table.</summary>
    private static float[] BoxBlur(Grid<ushort> height, GridRect rect, int k)
    {
        // Pad by k so the blur near the rect edge still sees real neighbours.
        var outer = new GridRect(rect.X0 - k, rect.Z0 - k, rect.X1 + k, rect.Z1 + k).Clamp(height.Width, height.Height);
        int ow = outer.X1 - outer.X0, oh = outer.Z1 - outer.Z0;
        var sat = new double[(ow + 1) * (oh + 1)];
        for (int z = 0; z < oh; z++)
        {
            double rowSum = 0;
            var row = height.Row(outer.Z0 + z);
            for (int x = 0; x < ow; x++)
            {
                rowSum += row[outer.X0 + x] / 257.0;
                sat[(x + 1) + (z + 1) * (ow + 1)] = sat[(x + 1) + z * (ow + 1)] + rowSum;
            }
        }

        int w = rect.X1 - rect.X0, h = rect.Z1 - rect.Z0;
        var result = new float[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int gx = rect.X0 + x - outer.X0, gz = rect.Z0 + z - outer.Z0;
                int x0 = Math.Max(gx - k, 0), x1 = Math.Min(gx + k + 1, ow);
                int z0 = Math.Max(gz - k, 0), z1 = Math.Min(gz + k + 1, oh);
                double sum = sat[x1 + z1 * (ow + 1)] - sat[x0 + z1 * (ow + 1)] - sat[x1 + z0 * (ow + 1)] + sat[x0 + z0 * (ow + 1)];
                result[x + z * w] = (float)(sum / ((x1 - x0) * (z1 - z0)));
            }
        return result;
    }
}
