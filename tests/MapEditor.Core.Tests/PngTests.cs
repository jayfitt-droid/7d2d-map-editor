using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

public sealed class PngTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-png-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Rgba_round_trips_with_every_filter(int? filter)
    {
        const int w = 37, h = 23;
        var pixels = new byte[w * h * 4];
        new Random(filter ?? 99).NextBytes(pixels);
        string path = Path.Combine(_dir, $"rgba{filter}.png");

        Png.WriteRows(path, w, h, PngPixelFormat.Rgba8,
            (y, row) => pixels.AsSpan(y * w * 4, w * 4).CopyTo(row), forceFilter: filter);

        var decoded = Png.ReadRgba(path, out int dw, out int dh);
        Assert.Equal((w, h), (dw, dh));
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Zero_alpha_pixels_keep_their_colour()
    {
        // splat4 stores water height in B with A = 0; premultiplying would wipe it.
        byte[] pixels = [0, 0, 30, 0, 0, 0, 0, 0];
        string path = Path.Combine(_dir, "water.png");
        Png.WriteRgba(path, 2, 1, pixels);
        Assert.Equal(pixels, Png.ReadRgba(path, out _, out _));
    }

    [Fact]
    public void Gray16_round_trips()
    {
        const int w = 64, h = 5;
        var values = new ushort[w * h];
        for (int i = 0; i < values.Length; i++) values[i] = (ushort)(i * 977);
        string path = Path.Combine(_dir, "gray16.png");

        Png.WriteRows(path, w, h, PngPixelFormat.Gray16, (y, row) =>
        {
            for (int x = 0; x < w; x++) BitConverter.TryWriteBytes(row[(x * 2)..], values[y * w + x]);
        });

        var info = Png.ReadInfo(path);
        Assert.Equal((16, 0), (info.BitDepth, info.ColorType));
        var decoded = new ushort[w * h];
        Png.ReadRows(path, PngPixelFormat.Gray16, null, (y, row) =>
        {
            for (int x = 0; x < w; x++) decoded[y * w + x] = BitConverter.ToUInt16(row[(x * 2)..]);
        });
        Assert.Equal(values, decoded);
    }
}
