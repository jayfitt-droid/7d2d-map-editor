using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapEditor.Core.GameData;

namespace MapEditor.App.Map;

/// <summary>
/// A stamp seen from above, for the Stamp tool's cursor: its heights shaded like hills, see-through where the stamp
/// fades out (its alpha), and blue where it brings water. North is up. Pictures are built in the background on first
/// use and kept.
/// </summary>
public static class StampPicture
{
    /// <summary>Longest side of a picture, in pixels; bigger stamps are sampled down.</summary>
    private const int MaxSize = 256;

    private static readonly ConcurrentDictionary<string, BitmapSource> Built = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> Building = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The picture of a stamp if it is ready; otherwise starts building it and calls <paramref name="ready"/> on the UI thread when done.</summary>
    public static BitmapSource? Get(StampLibrary library, string name, Action ready)
    {
        if (Built.TryGetValue(name, out var picture)) return picture;
        if (!Building.TryAdd(name, true)) return null;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        Task.Run(() =>
        {
            try
            {
                Built[name] = Build(library.Get(name));
                dispatcher.BeginInvoke(ready);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or UnauthorizedAccessException)
            {
                // No picture; the cursor stays an outline.
            }
            finally
            {
                Building.TryRemove(name, out _);
            }
        });
        return null;
    }

    public static BitmapSource Build(StampImage stamp)
    {
        int n = Math.Min(stamp.Size, MaxSize);
        float step = stamp.Size / (float)n;
        var heights = new float[n * n];
        var alphas = new float[n * n];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sx = (x + 0.5f) * step, sy = (y + 0.5f) * step;
                int i = y * n + x;
                heights[i] = stamp.SampleHeight(sx, sy);
                alphas[i] = Math.Clamp(stamp.SampleAlpha(sx, sy), 0, 1);
                if (alphas[i] < 0.05f) continue;
                lo = Math.Min(lo, heights[i]);
                hi = Math.Max(hi, heights[i]);
            }
        if (lo > hi) (lo, hi) = (0, 1);
        float range = Math.Max(hi - lo, 1);

        // Row 0 of a stamp is its south edge, so it is drawn bottom up.
        var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float height = (heights[i] - lo) / range;
                // Light from the north-west, as on the map.
                float east = heights[y * n + Math.Min(n - 1, x + 1)] - heights[y * n + Math.Max(0, x - 1)];
                float north = heights[Math.Min(n - 1, y + 1) * n + x] - heights[Math.Max(0, y - 1) * n + x];
                float shade = Math.Clamp(0.55f + (north - east) / range * 20 * n / 256f, 0, 1);
                float lum = 0.25f + 0.45f * height + 0.3f * shade;
                float r = lum, g = lum * 0.95f, b = lum * 0.85f;
                float water = stamp.HasWater ? Math.Clamp(stamp.SampleWater((x + 0.5f) * step, (y + 0.5f) * step), 0, 1) : 0;
                if (water > 0)
                {
                    r += (0.15f - r) * water;
                    g += (0.4f - g) * water;
                    b += (0.85f - b) * water;
                }
                float a = alphas[i];
                int o = ((n - 1 - y) * n + x) * 4;
                // Premultiplied BGRA.
                pixels[o] = (byte)(Math.Clamp(b, 0, 1) * a * 255);
                pixels[o + 1] = (byte)(Math.Clamp(g, 0, 1) * a * 255);
                pixels[o + 2] = (byte)(Math.Clamp(r, 0, 1) * a * 255);
                pixels[o + 3] = (byte)(a * 255);
            }
        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, pixels, n * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
