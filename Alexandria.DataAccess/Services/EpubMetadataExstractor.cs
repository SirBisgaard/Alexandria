using System.IO.Compression;
using System.Xml.Linq;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.DataAccess.Services;

public class EpubMetadataExstractor : IEpubMetadataExstractor
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    /// <summary>
    /// Extracts metadata from an EPUB file. Optionally extracts the cover image to disk.
    /// </summary>
    /// <param name="epubPath">Absolute or relative path to the .epub file.</param>
    /// <param name="coverOutputDirectory">
    /// Directory where the cover image should be saved.
    /// </param>
    public EpubMetadata Extract(string epubPath, string coverOutputDirectory)
    {
        if (!File.Exists(epubPath))
            throw new FileNotFoundException("EPUB file not found.", epubPath);

        using var zip = ZipFile.OpenRead(epubPath);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        using var opfStream = opfEntry.Open();
        var opfDoc = XDocument.Load(opfStream);

        var (metadata, metadataEl) = ParseMetadata(opfDoc);

        ExtractCover(zip, opfDoc, opfPath, coverOutputDirectory, metadataEl, metadata);

        return metadata;
    }

    private static IEnumerable<XElement> MetaElements(XElement metadataEl)
        => metadataEl.Elements(Opf + "meta").Concat(metadataEl.Elements("meta"));

    private static string GetOpfPath(ZipArchive zip)
    {
        var containerEntry = zip.GetEntry("META-INF/container.xml")
            ?? throw new InvalidOperationException("META-INF/container.xml not found – not a valid EPUB.");

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);

        var rootFile = doc
            .Descendants(Container + "rootfile")
            .FirstOrDefault(rf =>
                string.Equals(
                    rf.Attribute("media-type")?.Value,
                    "application/oebps-package+xml",
                    StringComparison.OrdinalIgnoreCase));

        return rootFile?.Attribute("full-path")?.Value
            ?? throw new InvalidOperationException("No OPF rootfile declared in container.xml.");
    }

    private static (EpubMetadata metadata, XElement metadataEl) ParseMetadata(XDocument opfDoc)
    {
        var metadataEl = opfDoc.Descendants(Opf + "metadata").FirstOrDefault()
            ?? throw new InvalidOperationException("No <metadata> element found in OPF.");

        var result = new EpubMetadata
        {
            Title = metadataEl.Elements(Dc + "title").FirstOrDefault()?.Value.Trim() ?? string.Empty,
            Language = metadataEl.Elements(Dc + "language").FirstOrDefault()?.Value.Trim() ?? string.Empty,
            PublicationDate = metadataEl.Elements(Dc + "date").FirstOrDefault()?.Value.Trim(),
            Description = metadataEl.Elements(Dc + "description").FirstOrDefault()?.Value.Trim(),
            Publisher = metadataEl.Elements(Dc + "publisher").FirstOrDefault()?.Value.Trim(),
            Authors = metadataEl
                .Elements(Dc + "creator")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList()
        };

        ParseSeries(metadataEl, result);

        return (result, metadataEl);
    }

    /// <summary>
    /// Attempts to read Calibre-style and EPUB 3 series metadata.
    /// </summary>
    private static void ParseSeries(XElement metadataEl, EpubMetadata result)
    {
        // ── Strategy 1: Calibre meta tags ──
        var calibreSeries = FindMetaByName(metadataEl, "calibre:series");
        if (calibreSeries is not null)
        {
            result.SeriesName = calibreSeries;
            var indexStr = FindMetaByName(metadataEl, "calibre:series_index");
            if (double.TryParse(indexStr, System.Globalization.CultureInfo.InvariantCulture, out var idx))
                result.SeriesIndex = (int)idx;
            return;
        }

        // ── Strategy 2: EPUB 3 <meta property="belongs-to-collection"> ──
        var collectionMeta = MetaElements(metadataEl)
            .FirstOrDefault(m =>
                string.Equals(
                    m.Attribute("property")?.Value,
                    "belongs-to-collection",
                    StringComparison.OrdinalIgnoreCase));

        if (collectionMeta is null)
            return;

        result.SeriesName = collectionMeta.Value.Trim();

        var refineId = collectionMeta.Attribute("id")?.Value;
        if (refineId is null)
            return;

        var positionMeta = MetaElements(metadataEl)
            .FirstOrDefault(m =>
                m.Attribute("refines")?.Value == $"#{refineId}" &&
                string.Equals(
                    m.Attribute("property")?.Value,
                    "group-position",
                    StringComparison.OrdinalIgnoreCase));

        if (positionMeta is not null && int.TryParse(positionMeta.Value.Trim(), out var pos))
            result.SeriesIndex = pos;
    }

    private static string? FindMetaByName(XElement metadataEl, string name)
    {
        return MetaElements(metadataEl)
            .FirstOrDefault(m =>
                string.Equals(m.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")?.Value?.Trim();
    }

    private static void ExtractCover(ZipArchive zip, XDocument opfDoc, string opfPath, string outputDirectory, XElement metadataEl, EpubMetadata metadata)
    {
        var coverHref = FindCoverHref(opfDoc, metadataEl);
        if (coverHref is null)
            return;

        // The href in the OPF manifest is relative to the OPF file's directory.
        var opfDirectory = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        var coverEntryPath = string.IsNullOrEmpty(opfDirectory)
            ? coverHref
            : $"{opfDirectory}/{coverHref}";

        // Normalise path separators and try to locate the entry.
        coverEntryPath = coverEntryPath.Replace('\\', '/');
        var coverEntry = zip.GetEntry(coverEntryPath)
            ?? zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, coverEntryPath, StringComparison.OrdinalIgnoreCase));

        if (coverEntry is null)
            return;

        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(coverEntry.FullName);
        if (string.IsNullOrEmpty(extension))
            extension = ".jpg";

        var outputPath = Path.Combine(outputDirectory, $"cover{extension}");

        using var entryStream = coverEntry.Open();
        using var fileStream = File.Create(outputPath);
        entryStream.CopyTo(fileStream);

        metadata.CoverExtracted = true;
        metadata.CoverFilePath = outputPath;
    }

    /// <summary>
    /// Locates the cover image href in the OPF manifest using multiple strategies.
    /// </summary>
    private static string? FindCoverHref(XDocument opfDoc, XElement metadataEl)
    {
        var manifest = opfDoc.Descendants(Opf + "manifest").FirstOrDefault();
        if (manifest is null)
            return null;

        var items = manifest.Elements(Opf + "item").ToList();

        // ── Strategy 1: <meta name="cover" content="cover-image-id" /> ──
        var coverId = FindMetaByName(metadataEl, "cover");
        if (coverId is not null)
        {
            var href = items
                .FirstOrDefault(i =>
                    string.Equals(i.Attribute("id")?.Value, coverId, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")?.Value;

            if (href is not null)
                return href;
        }

        // ── Strategy 2: manifest item with properties="cover-image" (EPUB 3) ──
        var coverItem = items.FirstOrDefault(i =>
            (i.Attribute("properties")?.Value ?? string.Empty)
                .Split(' ')
                .Any(p => string.Equals(p, "cover-image", StringComparison.OrdinalIgnoreCase)));

        return coverItem?.Attribute("href")?.Value;
    }
}
