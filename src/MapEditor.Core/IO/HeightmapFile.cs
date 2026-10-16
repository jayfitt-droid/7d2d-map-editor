using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Heightmap import/export for use with other tools (World Machine, Gaea, image editors).</summary>
public static class HeightmapFile
{
    /// <summary>
    /// Writes the heightmap. ".raw" uses the game's dtm format (uint16 = metres * 257, rows from the south);
    /// ".png" writes a north-up 16-bit greyscale image with the same values.
    /// </summary>
    public static void Export(Grid<ushort> height, string path)
    {
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Png.WriteRows(path, height.Width, height.Height, PngPixelFormat.Gray16, (y, row) =>
            {
                var src = height.Row(height.Height - 1 - y);
                for (int x = 0; x < src.Length; x++)
                {
                    row[x * 2] = (byte)src[x];
                    row[x * 2 + 1] = (byte)(src[x] >> 8);
                }
            });
        }
        else
        {
            HeightRaw.Write(path, height);
        }
    }

    /// <summary>
    /// Reads a heightmap of exactly <paramref name="size"/> square. 16-bit PNGs and .raw files use the game's
    /// units (metres * 257); 8-bit PNGs are read as 1 metre per step.
    /// </summary>
    public static Grid<ushort> Import(string path, int size)
    {
        Grid<ushort> grid;
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            var info = Png.ReadInfo(path);
            if (info.Width != size || info.Height != size)
                throw new InvalidDataException($"The image is {info.Width}x{info.Height}; this world needs {size}x{size}.");
            grid = new Grid<ushort>(size, size);
            if (info.ColorType == 0 && info.BitDepth == 16)
            {
                Png.ReadRows(path, PngPixelFormat.Gray16, null, (y, row) =>
                {
                    var dst = grid.Row(size - 1 - y);
                    for (int x = 0; x < size; x++) dst[x] = (ushort)(row[x * 2] | row[x * 2 + 1] << 8);
                });
            }
            else
            {
                Png.ReadRows(path, PngPixelFormat.Rgba8, null, (y, row) =>
                {
                    var dst = grid.Row(size - 1 - y);
                    for (int x = 0; x < size; x++) dst[x] = (ushort)(row[x * 4] * 257);
                });
            }
        }
        else
        {
            grid = HeightRaw.Read(path);
            if (grid.Width != size)
                throw new InvalidDataException($"The heightmap is {grid.Width}x{grid.Height}; this world needs {size}x{size}.");
        }
        return grid;
    }
}
