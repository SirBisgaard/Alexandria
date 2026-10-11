using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using ImageMagick;
using ImageMagick.Drawing;

namespace Alexandria.Tests.TestData;

/// <summary>
/// Generates synthetic comic archives for tests — every page is a drawn image, so no copyrighted
/// art is involved. Page widths encode the page number (<see cref="GrayPage"/>), which lets tests
/// recover reading order from a built EPUB by looking at image sizes alone.
/// </summary>
public static class ComicFixtures
{
    /// <summary>
    /// A black-and-white page: white with black blocks in two opposite corners, so it has full
    /// contrast and art touching every edge (margin cropping leaves its size alone).
    /// </summary>
    public static byte[] GrayPage(int pageNumber, int height = 900)
    {
        var width = 600 + pageNumber;
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        new Drawables().FillColor(MagickColors.Black).Rectangle(0, 0, 300, 300).Rectangle(width - 300, height - 300, width, height).Draw(image);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    /// <summary>A page that is coloured almost everywhere.</summary>
    public static byte[] ColorPage(int width = 600, int height = 900)
    {
        using var image = new MagickImage(MagickColors.Red, (uint)width, (uint)height);
        new Drawables().FillColor(MagickColors.Blue).Rectangle(0, height / 2.0, width, height).Draw(image);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    /// <summary>A landscape two-page spread: left half red, right half blue.</summary>
    public static byte[] SpreadPage(int width = 1600, int height = 1000)
    {
        using var image = new MagickImage(MagickColors.Red, (uint)width, (uint)height);
        new Drawables().FillColor(MagickColors.Blue).Rectangle(width / 2.0, 0, width, height).Draw(image);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    /// <summary>A page with a <paramref name="content"/>-coloured block on a <paramref name="background"/> page, leaving margins around it.</summary>
    public static byte[] MarginPage(int width, int height, int left, int top, int right, int bottom, MagickColor background, MagickColor content)
    {
        using var image = new MagickImage(background, (uint)width, (uint)height);
        new Drawables().FillColor(content).Rectangle(left, top, right, bottom).Draw(image);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    public static string ComicInfo(string body) => $"""<?xml version="1.0" encoding="utf-8"?><ComicInfo>{body}</ComicInfo>""";

    public static string CreateCbz(string folder, string fileName, IEnumerable<(string Name, byte[] Data)> entries)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        using var file = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var stream = entry.Open();
            stream.Write(data);
        }
        return path;
    }

    /// <summary>Three gray pages (1, 2, 3) and optional ComicInfo.xml.</summary>
    public static string CreateSimpleCbz(string folder, string fileName, string? comicInfoBody = null)
    {
        var entries = new List<(string, byte[])> { ("1.jpg", GrayPage(1)), ("2.jpg", GrayPage(2)), ("3.jpg", GrayPage(3)) };
        if (comicInfoBody is not null)
            entries.Add(("ComicInfo.xml", Encoding.UTF8.GetBytes(ComicInfo(comicInfoBody))));
        return CreateCbz(folder, fileName, entries);
    }

    /// <summary>
    /// Writes a RAR 4.x archive with every entry in "store" mode (no compression). Hand-rolled
    /// because RAR archives can only be created with the proprietary rar tool; the store method
    /// is fully specified by the RAR 4 technote, so SharpCompress reads these like real archives.
    /// </summary>
    public static string CreateStoredRar(string folder, string fileName, IEnumerable<(string Name, byte[] Data)> entries)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        using var output = new FileStream(path, FileMode.CreateNew);
        output.Write([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00]);
        WriteBlock(output, 0x73, 0x0000, new byte[6]);

        var dosTime = (uint)((2026 - 1980) << 25 | 1 << 21 | 1 << 16);
        foreach (var (name, data) in entries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name.Replace('/', '\\'));
            var fields = new MemoryStream();
            using (var writer = new BinaryWriter(fields, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write((uint)data.Length);
                writer.Write((uint)data.Length);
                writer.Write((byte)0);
                writer.Write(Crc32.HashToUInt32(data));
                writer.Write(dosTime);
                writer.Write((byte)20);
                writer.Write((byte)0x30);
                writer.Write((ushort)nameBytes.Length);
                writer.Write(0x20u);
                writer.Write(nameBytes);
            }
            WriteBlock(output, 0x74, 0x8000, fields.ToArray());
            output.Write(data);
        }

        WriteBlock(output, 0x7B, 0x4000, []);
        return path;
    }

    /// <summary>Block layout: CRC16 (low half of CRC32 over everything after it), type, flags, header size, fields.</summary>
    private static void WriteBlock(Stream output, byte type, ushort flags, byte[] fields)
    {
        var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(type);
            writer.Write(flags);
            writer.Write((ushort)(7 + fields.Length));
            writer.Write(fields);
        }
        var body = header.ToArray();
        output.Write(BitConverter.GetBytes((ushort)(Crc32.HashToUInt32(body) & 0xFFFF)));
        output.Write(body);
    }
}
