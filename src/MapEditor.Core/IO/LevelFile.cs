using System.Buffers.Binary;
using System.Text;
using MapEditor.Core.GameData;

namespace MapEditor.Core.IO;

/// <summary>
/// main.ttw handling. A freshly generated world's main.ttw holds only a WorldState header: magic
/// "ttw\0", a format version, the game version string, empty game state, and (last) a random world
/// GUID written as a length-prefixed 32-character hex string. Apart from the GUID it is identical for
/// every world made by the same game version, so the editor clones one and gives it a new GUID.
/// </summary>
public static class LevelFile
{
    public sealed record Candidate(string Path, int FormatVersion, string GameVersion, DateTime Modified);

    /// <summary>Reads the header of a main.ttw, or null if it is not one.</summary>
    public static Candidate? ReadHeader(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 9 || bytes[0] != 't' || bytes[1] != 't' || bytes[2] != 'w' || bytes[3] != 0) return null;
            int version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            int len = bytes[8]; // short strings: a single 7-bit length byte
            string gameVersion = len < 128 && 9 + len <= bytes.Length ? Encoding.UTF8.GetString(bytes, 9, len) : "";
            return new Candidate(path, version, gameVersion, File.GetLastWriteTimeUtc(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the best template: the newest format version, then the most recently written file. Small files
    /// (the generated-world kind) are preferred over hand-made worlds whose state includes other data.
    /// </summary>
    public static Candidate? FindTemplate(GameInstall? install)
    {
        var folders = new List<string>();
        if (Directory.Exists(GameInstall.UserGeneratedWorldsDir)) folders.AddRange(Directory.EnumerateDirectories(GameInstall.UserGeneratedWorldsDir));
        if (install is not null && Directory.Exists(install.WorldsDir)) folders.AddRange(Directory.EnumerateDirectories(install.WorldsDir));

        return folders
            .Select(f => Path.Combine(f, WorldFiles.Level))
            .Where(f => File.Exists(f) && new FileInfo(f).Length < 4096)
            .Select(ReadHeader)
            .OfType<Candidate>()
            .OrderByDescending(c => c.FormatVersion)
            .ThenByDescending(c => c.Modified)
            .FirstOrDefault();
    }

    /// <summary>Returns a copy of <paramref name="template"/> with a new world GUID, if it ends with one.</summary>
    public static byte[] WithNewGuid(byte[] template)
    {
        var copy = (byte[])template.Clone();
        if (!EndsWithGuid(copy)) return copy;
        var guid = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N").ToUpperInvariant());
        guid.CopyTo(copy, copy.Length - 32);
        return copy;
    }

    internal static bool EndsWithGuid(byte[] bytes)
    {
        if (bytes.Length < 33 || bytes[^33] != 32) return false;
        foreach (byte b in bytes.AsSpan(bytes.Length - 32))
            if (!char.IsAsciiHexDigitUpper((char)b) && !char.IsAsciiDigit((char)b)) return false;
        return true;
    }
}
