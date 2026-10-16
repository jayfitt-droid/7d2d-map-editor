using System.Runtime.InteropServices;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>dtm.raw: headerless little-endian uint16, row 0 = south edge, value = metres * 257.</summary>
public static class HeightRaw
{
    public static Grid<ushort> Read(string path)
    {
        long length = new FileInfo(path).Length;
        int size = (int)Math.Sqrt(length / 2);
        if ((long)size * size * 2 != length)
            throw new InvalidDataException($"{Path.GetFileName(path)} is {length} bytes, which is not a square uint16 heightmap.");

        var data = new ushort[size * size];
        using (var stream = File.OpenRead(path))
            stream.ReadExactly(MemoryMarshal.AsBytes(data.AsSpan()));
        if (!BitConverter.IsLittleEndian) SwapAll(data);
        return new Grid<ushort>(size, size, data);
    }

    public static void Write(string path, Grid<ushort> height)
    {
        using var stream = File.Create(path);
        if (BitConverter.IsLittleEndian)
        {
            stream.Write(MemoryMarshal.AsBytes(height.Data.AsSpan()));
            return;
        }
        var copy = (ushort[])height.Data.Clone();
        SwapAll(copy);
        stream.Write(MemoryMarshal.AsBytes(copy.AsSpan()));
    }

    private static void SwapAll(ushort[] data)
    {
        for (int i = 0; i < data.Length; i++) data[i] = (ushort)((data[i] >> 8) | (data[i] << 8));
    }
}
