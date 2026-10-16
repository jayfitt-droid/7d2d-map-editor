using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MapEditor.Core.IO;

/// <summary>Pixel layout handed to <see cref="Png"/> row callbacks.</summary>
public enum PngPixelFormat
{
    /// <summary>4 bytes per pixel, R G B A, 8 bits each.</summary>
    Rgba8,
    /// <summary>2 bytes per pixel, little-endian 16-bit grey (as stored in memory, not on disk).</summary>
    Gray16,
}

public sealed record PngInfo(int Width, int Height, int BitDepth, int ColorType);

/// <summary>
/// Minimal, dependency-free PNG codec. It never premultiplies alpha, so the game's
/// splat4 (water in B with A=0) survives a round trip. Rows are top-down, as in the file.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngInfo ReadInfo(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadHeader(stream);
    }

    /// <summary>Decodes the whole image to top-down RGBA8.</summary>
    public static byte[] ReadRgba(string path, out int width, out int height)
    {
        byte[]? pixels = null;
        int w = 0;
        ReadRows(path, PngPixelFormat.Rgba8, (info) =>
        {
            w = info.Width;
            pixels = new byte[(long)info.Width * info.Height * 4 <= int.MaxValue
                ? info.Width * info.Height * 4
                : throw new NotSupportedException("Image too large to decode into one buffer.")];
        }, (y, row) => row.CopyTo(pixels.AsSpan(y * w * 4)));
        width = w;
        height = pixels!.Length / (w * 4);
        return pixels;
    }

    /// <summary>
    /// Streams decoded rows (top-down) in the requested format without buffering the whole image.
    /// </summary>
    public static void ReadRows(string path, PngPixelFormat format, Action<PngInfo>? onHeader, RowAction onRow)
    {
        using var stream = new BufferedStream(File.OpenRead(path), 1 << 20);
        var info = ReadHeader(stream);
        ValidateSupported(info, format);
        onHeader?.Invoke(info);

        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        using var idat = new IdatStream(stream, (type, data) =>
        {
            if (type == "PLTE") palette = data;
            else if (type == "tRNS") paletteAlpha = data;
        });

        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        int bpp = BytesPerPixel(info);
        int stride = checked(info.Width * bpp);
        var prev = new byte[stride];
        var cur = new byte[stride];
        int outBpp = format == PngPixelFormat.Rgba8 ? 4 : 2;
        var output = new byte[checked(info.Width * outBpp)];

        for (int y = 0; y < info.Height; y++)
        {
            int filter = z.ReadByte();
            if (filter < 0) throw new InvalidDataException("PNG data ended early.");
            z.ReadExactly(cur);
            Unfilter((byte)filter, cur, prev, bpp);
            if (format == PngPixelFormat.Rgba8)
                ConvertToRgba8(info, cur, output, palette, paletteAlpha);
            else
                ConvertToGray16(cur, output);
            onRow(y, output);
            (prev, cur) = (cur, prev);
        }
    }

    public delegate void RowAction(int y, ReadOnlySpan<byte> row);
    public delegate void RowSource(int y, Span<byte> row);

    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba, CompressionLevel level = CompressionLevel.Fastest)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("Buffer size does not match dimensions.", nameof(rgba));
        unsafe
        {
            fixed (byte* p = rgba)
            {
                var ptr = (IntPtr)p;
                WriteRows(path, width, height, PngPixelFormat.Rgba8, (y, row) =>
                    new ReadOnlySpan<byte>((byte*)ptr + (long)y * width * 4, width * 4).CopyTo(row), level);
            }
        }
    }

    /// <summary>Encodes an image whose top-down rows are produced on demand.</summary>
    public static void WriteRows(string path, int width, int height, PngPixelFormat format, RowSource source,
        CompressionLevel level = CompressionLevel.Fastest, int? forceFilter = null)
    {
        using var file = new BufferedStream(File.Create(path), 1 << 20);
        WriteRows(file, width, height, format, source, level, forceFilter);
    }

    public static void WriteRows(Stream output, int width, int height, PngPixelFormat format, RowSource source,
        CompressionLevel level = CompressionLevel.Fastest, int? forceFilter = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = format == PngPixelFormat.Rgba8 ? (byte)8 : (byte)16;
        ihdr[9] = format == PngPixelFormat.Rgba8 ? (byte)6 : (byte)0;
        ihdr[10] = ihdr[11] = ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        int bpp = format == PngPixelFormat.Rgba8 ? 4 : 2;
        int stride = width * bpp;
        var raw = new byte[stride];
        var prev = new byte[stride];
        var filtered = new byte[5][];
        for (int i = 0; i < 5; i++) filtered[i] = new byte[stride];

        using (var idat = new IdatWriter(output))
        using (var z = new ZLibStream(idat, level, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                source(y, raw);
                if (format == PngPixelFormat.Gray16) SwapToBigEndian16(raw);
                int best = forceFilter ?? ChooseFilter(raw, prev, bpp, filtered);
                if (forceFilter is int f) ApplyFilter(f, raw, prev, bpp, filtered[f]);
                z.WriteByte((byte)best);
                z.Write(filtered[best]);
                (prev, raw) = (raw, prev);
            }
        }

        WriteChunk(output, "IEND", []);
    }

    // ---- decoding helpers -------------------------------------------------

    private static PngInfo ReadHeader(Stream stream)
    {
        Span<byte> sig = stackalloc byte[8];
        stream.ReadExactly(sig);
        if (!sig.SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG file.");
        var (type, data) = ReadChunk(stream);
        if (type != "IHDR" || data.Length < 13) throw new InvalidDataException("PNG is missing IHDR.");
        int width = BinaryPrimitives.ReadInt32BigEndian(data);
        int height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
        if (data[12] != 0) throw new NotSupportedException("Interlaced PNGs are not supported.");
        return new PngInfo(width, height, data[8], data[9]);
    }

    private static void ValidateSupported(PngInfo info, PngPixelFormat format)
    {
        bool ok = info.ColorType switch
        {
            0 => info.BitDepth is 8 or 16,
            2 or 4 or 6 => info.BitDepth is 8 or 16,
            3 => info.BitDepth == 8,
            _ => false,
        };
        if (!ok) throw new NotSupportedException($"PNG colour type {info.ColorType} at {info.BitDepth}-bit is not supported.");
        if (format == PngPixelFormat.Gray16 && !(info.ColorType == 0 && info.BitDepth == 16))
            throw new NotSupportedException("Gray16 output needs a 16-bit greyscale PNG.");
    }

    private static int BytesPerPixel(PngInfo info)
    {
        int channels = info.ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        return channels * info.BitDepth / 8;
    }

    private static void Unfilter(byte filter, Span<byte> cur, ReadOnlySpan<byte> prev, int bpp)
    {
        switch (filter)
        {
            case 0: return;
            case 1:
                for (int i = bpp; i < cur.Length; i++) cur[i] += cur[i - bpp];
                return;
            case 2:
                for (int i = 0; i < cur.Length; i++) cur[i] += prev[i];
                return;
            case 3:
                for (int i = 0; i < bpp; i++) cur[i] += (byte)(prev[i] >> 1);
                for (int i = bpp; i < cur.Length; i++) cur[i] += (byte)((cur[i - bpp] + prev[i]) >> 1);
                return;
            case 4:
                for (int i = 0; i < bpp; i++) cur[i] += prev[i];
                for (int i = bpp; i < cur.Length; i++) cur[i] += Paeth(cur[i - bpp], prev[i], prev[i - bpp]);
                return;
            default:
                throw new InvalidDataException($"Unknown PNG filter {filter}.");
        }
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void ConvertToRgba8(PngInfo info, ReadOnlySpan<byte> src, Span<byte> dst, byte[]? palette, byte[]? paletteAlpha)
    {
        int w = info.Width;
        int step = info.BitDepth / 8; // 1 or 2; for 16-bit we take the high byte
        switch (info.ColorType)
        {
            case 6:
                if (step == 1) { src.CopyTo(dst); return; }
                for (int x = 0; x < w * 4; x++) dst[x] = src[x * 2];
                return;
            case 2:
                for (int x = 0; x < w; x++)
                {
                    dst[x * 4] = src[x * 3 * step];
                    dst[x * 4 + 1] = src[(x * 3 + 1) * step];
                    dst[x * 4 + 2] = src[(x * 3 + 2) * step];
                    dst[x * 4 + 3] = 255;
                }
                return;
            case 0:
                for (int x = 0; x < w; x++)
                {
                    byte g = src[x * step];
                    dst[x * 4] = dst[x * 4 + 1] = dst[x * 4 + 2] = g;
                    dst[x * 4 + 3] = 255;
                }
                return;
            case 4:
                for (int x = 0; x < w; x++)
                {
                    byte g = src[x * 2 * step];
                    dst[x * 4] = dst[x * 4 + 1] = dst[x * 4 + 2] = g;
                    dst[x * 4 + 3] = src[(x * 2 + 1) * step];
                }
                return;
            case 3:
                if (palette is null) throw new InvalidDataException("Palette PNG has no PLTE chunk.");
                for (int x = 0; x < w; x++)
                {
                    int i = src[x];
                    if (i * 3 + 2 >= palette.Length) throw new InvalidDataException("Palette index out of range.");
                    dst[x * 4] = palette[i * 3];
                    dst[x * 4 + 1] = palette[i * 3 + 1];
                    dst[x * 4 + 2] = palette[i * 3 + 2];
                    dst[x * 4 + 3] = paletteAlpha is not null && i < paletteAlpha.Length ? paletteAlpha[i] : (byte)255;
                }
                return;
        }
    }

    private static void ConvertToGray16(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int i = 0; i + 1 < src.Length; i += 2)
        {
            dst[i] = src[i + 1];
            dst[i + 1] = src[i];
        }
    }

    private static (string Type, byte[] Data) ReadChunk(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        stream.ReadExactly(head);
        int length = BinaryPrimitives.ReadInt32BigEndian(head);
        if (length < 0) throw new InvalidDataException("Bad PNG chunk length.");
        string type = Encoding.ASCII.GetString(head[4..]);
        var data = new byte[length];
        stream.ReadExactly(data);
        Span<byte> crc = stackalloc byte[4];
        stream.ReadExactly(crc);
        return (type, data);
    }

    /// <summary>Presents the concatenated IDAT payloads as one stream; other chunks go to a callback.</summary>
    private sealed class IdatStream(Stream source, Action<string, byte[]> onOtherChunk) : Stream
    {
        private byte[] _chunk = [];
        private int _pos;
        private bool _done;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_pos >= _chunk.Length)
            {
                if (_done) return 0;
                var (type, data) = ReadChunk(source);
                if (type == "IDAT") { _chunk = data; _pos = 0; }
                else if (type == "IEND") _done = true;
                else onOtherChunk(type, data);
            }
            int n = Math.Min(buffer.Length, _chunk.Length - _pos);
            _chunk.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---- encoding helpers -------------------------------------------------

    private static int ChooseFilter(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> prev, int bpp, byte[][] filtered)
    {
        int best = 0;
        long bestScore = long.MaxValue;
        // None, Sub and Up cover the game's mostly-flat masks well; Paeth helps hillshade-like data.
        foreach (int f in (ReadOnlySpan<int>)[0, 1, 2, 4])
        {
            ApplyFilter(f, raw, prev, bpp, filtered[f]);
            long score = 0;
            foreach (byte b in filtered[f]) score += b < 128 ? b : 256 - b;
            if (score < bestScore) { bestScore = score; best = f; }
            if (score == 0) break;
        }
        return best;
    }

    private static void ApplyFilter(int filter, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> prev, int bpp, Span<byte> dst)
    {
        switch (filter)
        {
            case 0: raw.CopyTo(dst); break;
            case 1:
                for (int i = 0; i < raw.Length; i++) dst[i] = (byte)(raw[i] - (i >= bpp ? raw[i - bpp] : 0));
                break;
            case 2:
                for (int i = 0; i < raw.Length; i++) dst[i] = (byte)(raw[i] - prev[i]);
                break;
            case 3:
                for (int i = 0; i < raw.Length; i++) dst[i] = (byte)(raw[i] - (((i >= bpp ? raw[i - bpp] : 0) + prev[i]) >> 1));
                break;
            case 4:
                for (int i = 0; i < raw.Length; i++)
                    dst[i] = (byte)(raw[i] - Paeth(i >= bpp ? raw[i - bpp] : (byte)0, prev[i], i >= bpp ? prev[i - bpp] : (byte)0));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(filter));
        }
    }

    private static void SwapToBigEndian16(Span<byte> row)
    {
        for (int i = 0; i + 1 < row.Length; i += 2) (row[i], row[i + 1]) = (row[i + 1], row[i]);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        Encoding.ASCII.GetBytes(type, head[4..]);
        output.Write(head);
        output.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, head[4..]), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    /// <summary>Buffers compressed bytes and emits them as IDAT chunks of up to 1 MB.</summary>
    private sealed class IdatWriter(Stream output) : Stream
    {
        private readonly byte[] _buffer = new byte[1 << 20];
        private int _count;

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            while (data.Length > 0)
            {
                int n = Math.Min(data.Length, _buffer.Length - _count);
                data[..n].CopyTo(_buffer.AsSpan(_count));
                _count += n;
                data = data[n..];
                if (_count == _buffer.Length) Flush();
            }
        }

        public override void Flush()
        {
            if (_count == 0) return;
            WriteChunk(output, "IDAT", _buffer.AsSpan(0, _count));
            _count = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Flush();
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
