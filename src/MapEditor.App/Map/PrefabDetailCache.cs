using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapEditor.Core.GameData;

namespace MapEditor.App.Map;

/// <summary>
/// Top-down pictures of prefabs for the map, built in the background the first time each one is wanted and kept
/// in memory and on disk (%LOCALAPPDATA%\7D2DMapEditor\PrefabDetail), so a busy world fills in once and is instant
/// after that. Pictures are keyed by the prefab's block file date and size, so a game update rebuilds them.
/// </summary>
public sealed class PrefabDetailCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "7D2DMapEditor", "PrefabDetail");

    private const int MaxWorkers = 2;

    private readonly PrefabLibrary _library;
    private readonly ConcurrentDictionary<string, PrefabTopView?> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentStack<string> _queue = new();
    private readonly Dictionary<(string Name, int Rotation), BitmapSource> _bitmaps = [];
    private int _workers;

    public PrefabDetailCache(PrefabLibrary library) => _library = library;

    /// <summary>Raised on a worker thread when a picture has been built.</summary>
    public event Action? Built;

    /// <summary>
    /// The picture of <paramref name="info"/> turned to <paramref name="rotation"/>, or null while it is still being
    /// built (it is queued) or if the prefab has no readable block file. Call on the UI thread.
    /// </summary>
    public BitmapSource? Get(PrefabInfo info, int rotation)
    {
        var key = (info.Name, rotation & 3);
        if (_bitmaps.TryGetValue(key, out var bitmap)) return bitmap;
        if (!_views.TryGetValue(info.Name, out var view))
        {
            Request(info.Name);
            return null;
        }
        if (view is null) return null;
        var turned = view.Turned(rotation);
        bitmap = BitmapSource.Create(turned.Width, turned.Height, 96, 96, PixelFormats.Pbgra32, null, turned.Pixels, turned.Width * 4);
        bitmap.Freeze();
        _bitmaps[key] = bitmap;
        return bitmap;
    }

    private void Request(string name)
    {
        if (!_pending.TryAdd(name, 0)) return;
        // Newest first: the prefabs on screen now matter more than ones scrolled past.
        _queue.Push(name);
        if (Interlocked.Increment(ref _workers) > MaxWorkers)
        {
            Interlocked.Decrement(ref _workers);
            return;
        }
        Task.Run(Work);
    }

    private void Work()
    {
        try
        {
            while (_queue.TryPop(out var name))
            {
                _views[name] = _library.Find(name) is { } info ? Load(info) : null;
                _pending.TryRemove(name, out _);
                Built?.Invoke();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _workers);
        }
        // A request can slip in between the last pop and the decrement.
        if (_queue.IsEmpty) return;
        if (Interlocked.Increment(ref _workers) <= MaxWorkers) Task.Run(Work);
        else Interlocked.Decrement(ref _workers);
    }

    private static PrefabTopView? Load(PrefabInfo info)
    {
        string tts = Path.Combine(info.Folder, info.Name + ".tts");
        string? cached = null;
        try
        {
            var file = new FileInfo(tts);
            if (!file.Exists) return null;
            cached = Path.Combine(CacheDir, $"{info.Name}-{file.LastWriteTimeUtc.Ticks ^ file.Length:x}.png");
            if (File.Exists(cached) && ReadPng(cached) is { } fromDisk) return fromDisk;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Fall through and build it.
        }

        var view = PrefabTopViewReader.Read(tts, info.YOffset);
        if (view is not null && cached is not null) WritePng(cached, view);
        return view;
    }

    private static PrefabTopView? ReadPng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource source = frame.Format == PixelFormats.Pbgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var pixels = new uint[source.PixelWidth * source.PixelHeight];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            return new PrefabTopView(source.PixelWidth, source.PixelHeight, pixels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
                                       or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static void WritePng(string path, PrefabTopView view)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var source = BitmapSource.Create(view.Width, view.Height, 96, 96, PixelFormats.Pbgra32, null, view.Pixels, view.Width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or System.Runtime.InteropServices.COMException)
        {
            // The disk cache is only a speed-up; the picture is still in memory.
        }
    }
}
