using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapEditor.Core.Rendering;

namespace MapEditor.App.Map;

/// <summary>Whole-world overview with the main view's viewport; click or drag to move the view.</summary>
public sealed class MiniMap : FrameworkElement
{
    private static readonly Pen ViewportPen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen BorderPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1));
    private BitmapSource? _overview;
    private MapView? _target;

    public MapView? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.ViewChanged -= OnTargetViewChanged;
            _target = value;
            if (_target is not null) _target.ViewChanged += OnTargetViewChanged;
            InvalidateVisual();
        }
    }

    /// <summary>Renders the overview from the top mip level on a background thread.</summary>
    public async void SetRenderer(MapRenderer? renderer, MapLayers layers)
    {
        _overview = null;
        InvalidateVisual();
        if (renderer is null) return;

        int top = renderer.Levels - 1;
        var bitmap = await Task.Run(() =>
        {
            var buffer = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
            renderer.RenderTile(top, 0, 0, layers & ~MapLayers.Radiation, buffer);
            int used = (int)Math.Ceiling(renderer.World.Size / (double)MapRenderer.MetresPerPixel(top));
            var full = BitmapSource.Create(MapRenderer.TileSize, MapRenderer.TileSize, 96, 96, PixelFormats.Pbgra32, null, buffer, MapRenderer.TileSize * 4);
            var cropped = new CroppedBitmap(full, new Int32Rect(0, 0, used, used));
            cropped.Freeze();
            return (BitmapSource)cropped;
        });
        if (_target?.Renderer != renderer) return;
        _overview = bitmap;
        InvalidateVisual();
    }

    private void OnTargetViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 20, 20, 24)), BorderPen, bounds);
        if (_overview is null || _target?.Renderer is not { } renderer) return;

        dc.DrawImage(_overview, bounds);
        double k = bounds.Width / renderer.World.Size;
        var v = _target.VisibleMapRect;
        var viewport = new Rect(v.X * k, v.Y * k, v.Width * k, v.Height * k);
        viewport.Intersect(bounds);
        if (!viewport.IsEmpty) dc.DrawRectangle(null, ViewportPen, viewport);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        MoveTarget(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) MoveTarget(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    private void MoveTarget(Point p)
    {
        if (_target?.Renderer is not { } renderer || ActualWidth <= 0) return;
        double k = renderer.World.Size / ActualWidth;
        _target.CenterOn(new Point(p.X * k, p.Y * k));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
