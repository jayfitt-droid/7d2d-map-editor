using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class RoadLevelTests
{
    private static World Ground(int size, Func<int, int, float> metres)
    {
        var world = new World { Name = "L", Size = size, Height = new Grid<ushort>(size, size) };
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++) world.Height[x, z] = World.MetresToHeight(metres(x, z));
        return world;
    }

    private static float H(World w, int x, int z) => World.HeightToMetres(w.Height[x, z]);

    [Fact]
    public void Road_on_flat_ground_is_levelled_to_a_whole_metre()
    {
        var world = Ground(512, (_, _) => 40.11f);
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Asphalt, 6), new EditSession("Road"));
        for (int x = 110; x <= 390; x += 20)
            Assert.Equal(40f, H(world, x, 200), 2);
    }

    [Fact]
    public void Road_over_gentle_undulation_is_flat()
    {
        // +-0.2 m waves every 80 m.
        var world = Ground(512, (x, _) => 40.3f + 0.2f * MathF.Sin(x / 80f * MathF.Tau));
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Asphalt, 6), new EditSession("Road"));
        float first = H(world, 150, 200);
        Assert.Equal(MathF.Round(first), first, 2);
        for (int x = 150; x <= 350; x += 10)
            Assert.Equal(first, H(world, x, 200), 2);
    }

    [Fact]
    public void Road_up_a_slope_is_left_smooth()
    {
        // A 5% grade: no flats, so no whole-metre steps.
        var world = Ground(512, (x, _) => 20 + x * 0.05f);
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Gravel, 4.5f), new EditSession("Road"));
        for (int x = 150; x < 350; x++)
        {
            float rise = H(world, x + 1, 200) - H(world, x, 200);
            Assert.InRange(rise, 0.02f, 0.08f);
        }
    }

    [Fact]
    public void Levelled_stretch_eases_into_a_slope_beyond_it()
    {
        // Flat at 40.4 m for x < 250, then climbing at 5%.
        var world = Ground(512, (x, _) => x < 250 ? 40.4f : 40.4f + (x - 250) * 0.05f);
        RoadOp.Draw(world, new RoadPath([new(50, 200), new(450, 200)], RoadType.Gravel, 4.5f), new EditSession("Road"));
        Assert.Equal(40f, H(world, 120, 200), 2);
        for (int x = 60; x < 440; x++)
            Assert.InRange(MathF.Abs(H(world, x + 1, 200) - H(world, x, 200)), 0, 0.12f);
    }

    [Fact]
    public void Profile_without_flats_is_unchanged()
    {
        var line = Enumerable.Range(0, 30).Select(i => new Vector2(i * 4, 0)).ToList();
        var heights = line.Select(p => 10 + p.X * 0.1f).ToArray();
        var before = (float[])heights.Clone();
        RoadOp.LevelFlats(heights, line, new float[heights.Length]);
        Assert.Equal(before, heights);
    }
    [Fact]
    public void Levelled_stretches_at_different_heights_are_joined_by_a_ramp_not_a_step()
    {
        // Rolling ground: whole-metre stretches at 39, 40 and 41 m lie next to each other along this road.
        var rnd = new Random(3);
        var phase = Enumerable.Range(0, 5).Select(_ => rnd.NextSingle() * 6.3f).ToArray();
        var world = Ground(1024, (x, z) => 40 + 1.5f * MathF.Sin(x / 170f + phase[0]) + 0.8f * MathF.Sin(x / 61f + phase[1])
            + 0.3f * MathF.Sin(x / 23f + phase[2]) + 0.15f * MathF.Sin(x / 7f + phase[3]) + 0.5f * MathF.Sin(z / 90f + phase[4]));
        var line = RoadOp.Centreline([new(100, 300), new(900, 300)], RoadOp.CornerRounding(RoadType.Asphalt));
        var profile = RoadOp.HeightProfile(world, line, RoadType.Asphalt);

        Assert.Contains(profile, h => h == 39f);
        Assert.Contains(profile, h => h == 40f);
        for (int i = 1; i < profile.Length; i++)
            Assert.InRange(MathF.Abs(profile[i] - profile[i - 1]) / Vector2.Distance(line[i - 1], line[i]), 0, 0.04f);
    }
}
