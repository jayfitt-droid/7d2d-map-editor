using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class RoadEndTests
{
    private static World WithRoad(RoadType type, float radius, params Vector2[] points)
    {
        var world = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        RoadOp.Draw(world, new RoadPath(points, type, radius), new EditSession("Road"));
        return world;
    }

    [Fact]
    public void Finds_the_end_of_a_straight_road_and_its_heading()
    {
        var world = WithRoad(RoadType.Asphalt, 9f, new(100, 300), new(400, 300));
        var end = RoadEnds.Find(world.Roads!, new Vector2(410, 305), 30);
        Assert.NotNull(end);
        Assert.InRange(end.Point.X, 398, 402);
        Assert.InRange(end.Point.Y, 299, 301);
        Assert.True(end.Direction.X > 0.99f);
        Assert.Equal(RoadType.Asphalt, end.Type);
        Assert.Equal(9f, end.Radius);

        var other = RoadEnds.Find(world.Roads!, new Vector2(95, 300), 30);
        Assert.NotNull(other);
        Assert.InRange(other.Point.X, 98, 102);
        Assert.True(other.Direction.X < -0.99f);
    }

    [Fact]
    public void Follows_a_bend_to_a_diagonal_end()
    {
        var world = WithRoad(RoadType.Gravel, 4.5f, new(100, 100), new(300, 100), new(450, 250));
        var end = RoadEnds.Find(world.Roads!, new Vector2(455, 255), 30);
        Assert.NotNull(end);
        Assert.InRange(Vector2.Distance(end.Point, new Vector2(450, 250)), 0, 3);
        Assert.True(Vector2.Dot(end.Direction, Vector2.Normalize(new Vector2(1, 1))) > 0.98f);
        Assert.Equal(RoadType.Gravel, end.Type);
        Assert.InRange(end.Radius, 4.5f, 5f);
    }

    [Fact]
    public void The_middle_of_a_road_is_not_an_end()
    {
        var world = WithRoad(RoadType.Asphalt, 9f, new(100, 300), new(400, 300));
        Assert.Null(RoadEnds.Find(world.Roads!, new Vector2(250, 310), 30));
        Assert.Null(RoadEnds.Find(world.Roads!, new Vector2(250, 400), 30));
    }
}
