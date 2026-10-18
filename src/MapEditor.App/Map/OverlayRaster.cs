using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MapEditor.App.Map;

/// <summary>
/// A screen-sized premultiplied BGRA bitmap for drawing many small outlines in software. WPF's GPU
/// tessellator draws stray spikes when a single geometry holds thousands of tiny rectangles.
/// </summary>
public sealed class OverlayRaster
{
    private WriteableBitmap? _bitmap;
    private uint[] _pixels = [];
    private int _width, _height;
    private double _dpi = 1;

    /// <summary>Resizes if needed and clears to transparent. Sizes are in DIPs.</summary>
    public void Begin(double widthDips, double heightDips, double dpi)
    {
        _dpi = dpi;
        int w = Math.Max(1, (int)Math.Ceiling(widthDips * dpi));
        int h = Math.Max(1, (int)Math.Ceiling(heightDips * dpi));
        if (_bitmap is null || w != _width || h != _height)
        {
            _width = w;
            _height = h;
            _bitmap = new WriteableBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32, null);
            _pixels = new uint[w * h];
        }
        else
        {
            Array.Clear(_pixels);
        }
    }

    /// <summary>Converts a straight (non-premultiplied) colour to the premultiplied pixel value.</summary>
    public static uint Premultiply(Color c)
    {
        uint a = c.A;
        return a << 24 | (uint)(c.R * a / 255) << 16 | (uint)(c.G * a / 255) << 8 | (uint)(c.B * a / 255);
    }

    /// <summary>Fills a rectangle given in DIPs, replacing what is under it.</summary>
    public void Fill(Rect dips, uint colour)
    {
        int x0 = ToPixel(dips.Left), y0 = ToPixel(dips.Top), x1 = ToPixel(dips.Right), y1 = ToPixel(dips.Bottom);
        if (x1 - x0 < 3 || y1 - y0 < 3) return;
        FillClipped(x0, y0, x1, y1, colour);
    }

    /// <summary>
    /// An outline with a one-pixel <paramref name="halo"/> either side of it, so a light line still shows on light
    /// ground and a dark one on dark ground.
    /// </summary>
    public void Outline(Rect dips, uint colour, uint halo)
    {
        int x0 = ToPixel(dips.Left), y0 = ToPixel(dips.Top), x1 = ToPixel(dips.Right), y1 = ToPixel(dips.Bottom);
        if (x1 - x0 >= 4 && y1 - y0 >= 4)
        {
            Ring(x0 - 1, y0 - 1, x1 + 1, y1 + 1, halo);
            Ring(x0 + 1, y0 + 1, x1 - 1, y1 - 1, halo);
        }
        else
        {
            FillClipped(x0 - 1, y0 - 1, x0 + 3, y0 + 3, halo);
        }
        Outline(dips, colour);
    }

    private void Ring(int x0, int y0, int x1, int y1, uint colour)
    {
        FillClipped(x0, y0, x1, y0 + 1, colour);
        FillClipped(x0, y1 - 1, x1, y1, colour);
        FillClipped(x0, y0, x0 + 1, y1, colour);
        FillClipped(x1 - 1, y0, x1, y1, colour);
    }

    /// <summary>Draws a one-device-pixel outline of a rectangle given in DIPs; tiny rectangles become 2x2 dots.</summary>
    public void Outline(Rect dips, uint colour)
    {
        // Clamp before converting so far-off-screen edges at high zoom cannot overflow int.
        int x0 = ToPixel(dips.Left), y0 = ToPixel(dips.Top);
        int x1 = ToPixel(dips.Right), y1 = ToPixel(dips.Bottom);
        if (x1 - x0 < 2 || y1 - y0 < 2)
        {
            FillClipped(x0, y0, x0 + 2, y0 + 2, colour);
            return;
        }
        FillClipped(x0, y0, x1, y0 + 1, colour);
        FillClipped(x0, y1 - 1, x1, y1, colour);
        FillClipped(x0, y0, x0 + 1, y1, colour);
        FillClipped(x1 - 1, y0, x1, y1, colour);
    }

    /// <summary>Fills a rectangle given in DIPs at least one device pixel thick either way (for thin lines).</summary>
    public void Bar(Rect dips, uint colour)
    {
        int x0 = ToPixel(dips.Left), y0 = ToPixel(dips.Top);
        FillClipped(x0, y0, Math.Max(x0 + 1, ToPixel(dips.Right)), Math.Max(y0 + 1, ToPixel(dips.Bottom)), colour);
    }

    private int ToPixel(double dip) => (int)Math.Floor(Math.Clamp(dip * _dpi, -1e6, 1e6));

    private void FillClipped(int x0, int y0, int x1, int y1, uint colour)
    {
        x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, _width); y1 = Math.Min(y1, _height);
        if (x0 >= x1 || y0 >= y1) return; // entirely off screen
        for (int y = y0; y < y1; y++)
            _pixels.AsSpan(y * _width + x0, x1 - x0).Fill(colour);
    }

    /// <summary>Uploads the pixels and draws the bitmap over the whole control.</summary>
    public void End(DrawingContext dc, Size dips)
    {
        if (_bitmap is null) return;
        _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
        dc.DrawImage(_bitmap, new Rect(0, 0, _width / _dpi, _height / _dpi));
    }
}
