using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Books.Comics;

namespace Alexandria.DataAccess.Externals;

/// <summary>
/// Reads and writes CBZ comic archives. Metadata lives in a root-level <c>ComicInfo.xml</c>
/// (the Anansi ComicInfo schema); when it is missing, title/authors/series fall back to the
/// file name parsed against the default rename template
/// <c>&lt;Authors&gt; - (&lt;Serie&gt; #&lt;SerieIndex&gt; - )&lt;Title&gt;</c>.
/// </summary>
public partial class CbzFileHandler : ICbzFileHandler
{
    public const string ComicInfoFileName = "ComicInfo.xml";

    /// <summary>
    /// ComicInfo element name → contributor role shown in Alexandria. Writer is not listed
    /// because writers are the book's authors.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ContributorElements = new Dictionary<string, string>
    {
        ["Penciller"] = "Penciller",
        ["Inker"] = "Inker",
        ["Colorist"] = "Colorist",
        ["Letterer"] = "Letterer",
        ["CoverArtist"] = "Cover Artist",
        ["Editor"] = "Editor",
        ["Translator"] = "Translator"
    };

    public static IReadOnlyList<string> ContributorRoleNames { get; } = ContributorElements.Values.ToList().AsReadOnly();

    /// <summary>
    /// Element order of the ComicInfo v2 XSD (<c>xs:sequence</c>). Rewritten documents are
    /// reordered to match so strict readers (ComicRack, Komga) accept them.
    /// </summary>
    private static readonly string[] SchemaOrder =
    [
        "Title", "Series", "Number", "Count", "Volume", "AlternateSeries", "AlternateNumber", "AlternateCount", "Summary", "Notes",
        "Year", "Month", "Day", "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor", "Translator",
        "Publisher", "Imprint", "Genre", "Tags", "Web", "PageCount", "LanguageISO", "Format", "BlackAndWhite", "Manga",
        "Characters", "Teams", "Locations", "ScanInformation", "StoryArc", "StoryArcNumber", "SeriesGroup", "AgeRating",
        "Pages", "CommunityRating", "MainCharacterOrTeam", "Review", "GTIN"
    ];

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif", ".jxl" };

    public (Book Metadata, byte[]? CoverBytes) Extract(string cbzPath)
    {
        using var stream = new FileStream(cbzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var pages = GetPageEntries(zip);
        var comicInfo = ReadComicInfo(zip);
        var book = ToBook(comicInfo, cbzPath, pages.Count);
        var coverEntry = FindCoverEntry(comicInfo, pages);
        return (book, coverEntry is null ? null : ReadAllBytes(coverEntry));
    }

    public Book ExtractMetadata(string cbzPath)
    {
        using var stream = new FileStream(cbzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return ToBook(ReadComicInfo(zip), cbzPath, GetPageEntries(zip).Count);
    }

    /// <summary>
    /// Writes <paramref name="book"/>'s metadata into the archive's ComicInfo.xml, keeping every
    /// element Alexandria does not manage (Pages, Tags, StoryArc, …). The archive is rewritten
    /// into a temp file and swapped in, because <see cref="ZipArchiveMode.Update"/> buffers every
    /// entry in memory — several hundred MB for a typical comic. Page images are stored without
    /// recompression since JPEG/PNG data does not deflate meaningfully.
    /// </summary>
    public void EmbedMetadata(string cbzPath, Book book)
    {
        var tempPath = cbzPath + ".alexandria-tmp";
        try
        {
            using (var source = new FileStream(cbzPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sourceZip = new ZipArchive(source, ZipArchiveMode.Read))
            using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var targetZip = new ZipArchive(target, ZipArchiveMode.Create))
            {
                var document = ReadComicInfo(sourceZip) ?? new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("ComicInfo"));
                ApplyBook(document.Root!, book);

                foreach (var entry in sourceZip.Entries)
                {
                    if (IsComicInfoEntry(entry))
                        continue;
                    var compression = IsImage(entry.FullName) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var copy = targetZip.CreateEntry(entry.FullName, compression);
                    copy.LastWriteTime = entry.LastWriteTime;
                    if (IsDirectory(entry))
                        continue;
                    using var input = entry.Open();
                    using var output = copy.Open();
                    input.CopyTo(output);
                }

                var infoEntry = targetZip.CreateEntry(ComicInfoFileName, CompressionLevel.Optimal);
                using var infoStream = infoEntry.Open();
                using var writer = XmlWriter.Create(infoStream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
                document.Save(writer);
            }

            File.Move(tempPath, cbzPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Returns the archive's page images in reading order. Zip entry order is unreliable (and
    /// CBR→CBZ conversion preserves RAR's arbitrary order), so pages are sorted naturally by
    /// path. Backslashes are treated as separators — archives zipped on Windows often store them.
    /// macOS resource forks, hidden files and thumbnail caches are skipped.
    /// </summary>
    public static List<ZipArchiveEntry> GetPageEntries(ZipArchive zip)
    {
        return zip.Entries
            .Where(e => !IsDirectory(e) && IsImage(e.FullName) && !IsJunk(NormalizePath(e.FullName)))
            .OrderBy(e => NormalizePath(e.FullName), NaturalStringComparer.Instance)
            .ToList();
    }

    public static string NormalizePath(string entryName) => entryName.Replace('\\', '/');

    public static bool IsImage(string entryName) => ImageExtensions.Contains(Path.GetExtension(entryName));

    private static bool IsJunk(string normalizedPath)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)))
            return true;
        var fileName = segments.Length > 0 ? segments[^1] : normalizedPath;
        return fileName.StartsWith('.') || fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    private static bool IsComicInfoEntry(ZipArchiveEntry entry) => Path.GetFileName(NormalizePath(entry.FullName)).Equals(ComicInfoFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefers a root-level ComicInfo.xml; falls back to one inside a subfolder.</summary>
    private static XDocument? ReadComicInfo(ZipArchive zip)
    {
        var entry = zip.Entries.FirstOrDefault(e => NormalizePath(e.FullName).Equals(ComicInfoFileName, StringComparison.OrdinalIgnoreCase))
            ?? zip.Entries.FirstOrDefault(IsComicInfoEntry);
        if (entry is null)
            return null;

        try
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            return document.Root?.Name.LocalName == "ComicInfo" ? document : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Honours <c>&lt;Page Image="n" Type="FrontCover"/&gt;</c> from the Pages block, where n is
    /// the zero-based index into the reading-order page list; otherwise the first page is the cover.
    /// </summary>
    private static ZipArchiveEntry? FindCoverEntry(XDocument? comicInfo, List<ZipArchiveEntry> pages)
    {
        if (pages.Count == 0)
            return null;

        var frontCover = comicInfo?.Root?.Element("Pages")?.Elements("Page")
            .FirstOrDefault(p => string.Equals((string?)p.Attribute("Type"), "FrontCover", StringComparison.OrdinalIgnoreCase));
        if (frontCover is not null && int.TryParse((string?)frontCover.Attribute("Image"), out var index) && index >= 0 && index < pages.Count)
            return pages[index];

        return pages[0];
    }

    private static Book ToBook(XDocument? comicInfo, string cbzPath, int pageCount)
    {
        var root = comicInfo?.Root;
        var fromName = ParseFileName(Path.GetFileNameWithoutExtension(cbzPath));

        var seriesName = Value(root, "Series") ?? fromName.Series;
        var seriesIndex = ParseNumber(Value(root, "Number")) ?? ParseNumber(Value(root, "Volume")) ?? (Value(root, "Series") is null ? fromName.Index : null);
        var writers = SplitList(Value(root, "Writer"));

        var book = new Book
        {
            Title = Value(root, "Title") ?? fromName.Title,
            Authors = (writers.Count > 0 ? writers : fromName.Authors).Select(name => new Author { Name = name }).ToList(),
            Language = Value(root, "LanguageISO") ?? string.Empty,
            Description = Value(root, "Summary"),
            Publisher = Value(root, "Publisher"),
            PublicationDate = FormatDate(Value(root, "Year"), Value(root, "Month"), Value(root, "Day")),
            Isbn = Value(root, "GTIN"),
            Subjects = SplitList(Value(root, "Genre")),
            Contributors = ContributorElements
                .SelectMany(pair => SplitList(Value(root, pair.Key)).Select(name => new Contributor { Name = name, Role = pair.Value }))
                .ToList(),
            Series = seriesName is null ? null : new Series { Name = seriesName, Index = seriesIndex },
            File = new BookFile
            {
                MediaType = MediaType.Comic,
                PageCount = pageCount,
                ReadingDirection = string.Equals(Value(root, "Manga"), "YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) ? ComicReadingDirection.RightToLeft : ComicReadingDirection.LeftToRight,
                ColorMode = ComicColorMode.Auto
            }
        };
        return book;
    }

    private static void ApplyBook(XElement root, Book book)
    {
        Set(root, "Title", book.Title);
        Set(root, "Series", book.Series?.Name);
        Set(root, "Number", book.Series?.Index?.ToString("G29", CultureInfo.InvariantCulture));
        Set(root, "Summary", book.Description);
        Set(root, "Writer", JoinList(book.Authors.Select(a => a.Name)));
        Set(root, "Publisher", book.Publisher);
        Set(root, "LanguageISO", book.Language);
        Set(root, "Genre", JoinList(book.Subjects));
        Set(root, "GTIN", book.Isbn);

        var dateParts = (book.PublicationDate ?? string.Empty).Split('-', StringSplitOptions.RemoveEmptyEntries);
        Set(root, "Year", dateParts.ElementAtOrDefault(0));
        Set(root, "Month", dateParts.Length > 1 ? int.Parse(dateParts[1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : null);
        Set(root, "Day", dateParts.Length > 2 ? int.Parse(dateParts[2], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : null);

        foreach (var (element, role) in ContributorElements)
            Set(root, element, JoinList(book.Contributors.Where(c => string.Equals(c.Role, role, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name)));

        var currentManga = Value(root, "Manga");
        if (book.File.ReadingDirection == ComicReadingDirection.RightToLeft)
            Set(root, "Manga", "YesAndRightToLeft");
        else if (string.Equals(currentManga, "YesAndRightToLeft", StringComparison.OrdinalIgnoreCase))
            Set(root, "Manga", "Yes");

        var ordered = root.Elements()
            .OrderBy(e => Array.IndexOf(SchemaOrder, e.Name.LocalName) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        root.RemoveNodes();
        root.Add(ordered);
    }

    private static void Set(XElement root, string name, string? value)
    {
        var existing = root.Element(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            existing?.Remove();
            return;
        }
        if (existing is null)
            root.Add(new XElement(name, value));
        else
            existing.Value = value;
    }

    private static string? Value(XElement? root, string name)
    {
        var value = root?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static List<string> SplitList(string? value) => value is null
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    private static string? JoinList(IEnumerable<string> values)
    {
        var joined = string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct());
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>ComicInfo numbers may use a decimal comma ("1,5") in files produced on non-English locales.</summary>
    private static decimal? ParseNumber(string? value) =>
        value is not null && decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? FormatDate(string? year, string? month, string? day)
    {
        if (!int.TryParse(year, out var y) || y <= 0)
            return null;
        if (!int.TryParse(month, out var m) || m is < 1 or > 12)
            return y.ToString("D4", CultureInfo.InvariantCulture);
        if (!int.TryParse(day, out var d) || d is < 1 or > 31)
            return $"{y:D4}-{m:D2}";
        return $"{y:D4}-{m:D2}-{d:D2}";
    }

    private static (string Title, List<string> Authors, string? Series, decimal? Index) ParseFileName(string name)
    {
        var parts = name.Split(" - ");
        if (parts.Length >= 3 && SeriesIndexRegex().Match(parts[1]) is { Success: true } seriesMatch)
            return (string.Join(" - ", parts[2..]), SplitList(parts[0]), seriesMatch.Groups["series"].Value, ParseNumber(seriesMatch.Groups["index"].Value));
        if (parts.Length >= 2)
            return (string.Join(" - ", parts[1..]), SplitList(parts[0]), null, null);
        if (SeriesIndexRegex().Match(name) is { Success: true } nameMatch)
            return (name, [], nameMatch.Groups["series"].Value, ParseNumber(nameMatch.Groups["index"].Value));
        return (name, [], null, null);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [GeneratedRegex(@"^(?<series>.+?) #(?<index>\d+(?:[.,]\d+)?)$")]
    private static partial Regex SeriesIndexRegex();
}
