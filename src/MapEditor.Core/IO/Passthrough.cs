using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>
/// Decides whether a passthrough image (an original the editor can't fully model) still matches its layer, so it can
/// be written back as it was. <see cref="Grid{T}.Version"/> counts every change, including Undo, so an edited and then
/// undone layer is compared with the original instead of being taken as changed.
/// </summary>
public static class Passthrough
{
    /// <summary>Whether <paramref name="file"/>'s original image should be written in place of the layer.</summary>
    public static bool Applies(World world, string file)
    {
        if (!world.PassthroughImages.TryGetValue(file, out var image)) return false;
        return file switch
        {
            // Biome maps with more than 255 colours have no layer at all; painting them is refused.
            WorldFiles.Biomes => world.Biomes is null,
            WorldFiles.Roads => world.Roads is { } roads && Matches(roads, image, LayerImages.DecodeRoads),
            WorldFiles.Water => world.Water is { } water && Matches(water, image, LayerImages.DecodeWater),
            _ => true,
        };
    }

    private delegate Grid<byte> Decoder(byte[] rgba, int width, int height, out bool canonical);

    private static bool Matches(Grid<byte> layer, RgbaImage image, Decoder decode)
    {
        if (layer.Version == 0) return true;
        if (layer.Width != image.Width || layer.Height != image.Height) return false;
        return decode(image.Pixels, image.Width, image.Height, out _).Data.AsSpan().SequenceEqual(layer.Data);
    }
}
