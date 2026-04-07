using System.IO.Compression;
using System.Xml.Linq;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Files;

namespace Alexandria.DataAccess.Services;

public class EpubMetadataHandler : IEpubMetadataHandler
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    public EpubMetadata ExtractMetadata(string epubPath)
    {
        if (!File.Exists(epubPath))
            throw new FileNotFoundException("EPUB file not found.", epubPath);

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        using var opfStream = opfEntry.Open();
        var opfDoc = XDocument.Load(opfStream);

        var (metadata, _) = ParseMetadata(opfDoc);
        return metadata;
    }

    /// <summary>
    /// Extracts metadata and cover from an EPUB in a single ZIP open.
    /// </summary>
    public (EpubMetadata Metadata, string? CoverFilePath) ExtractAll(string epubPath, string coverOutputDirectory)
    {
        if (!File.Exists(epubPath))
        {
            throw new FileNotFoundException("EPUB file not found.", epubPath);
        }

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        using var opfStream = opfEntry.Open();
        var opfDoc = XDocument.Load(opfStream);

        var (metadata, metadataEl) = ParseMetadata(opfDoc);
        var coverFilePath = WriteCoverToDisk(zip, opfDoc, opfPath, coverOutputDirectory, metadataEl, metadata.AlexandriaUuid ?? metadata.GenerateDeterministicUuid());

        return (metadata, coverFilePath);
    }

    public void EmbedMetadata(string epubPath, EpubMetadata metadata)
    {
        if (!File.Exists(epubPath))
        {
            throw new FileNotFoundException("EPUB file not found.", epubPath);
        }

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
        {
            opfDoc = XDocument.Load(opfStream);
        }

        var metadataEl = opfDoc.Descendants(Opf + "metadata").FirstOrDefault()
            ?? throw new InvalidOperationException("No <metadata> element found in OPF.");

        // Update DC elements.
        SetDcElement(metadataEl, "title", metadata.Title);
        SetDcElement(metadataEl, "language", metadata.Language);
        SetDcElement(metadataEl, "date", metadata.PublicationDate);
        SetDcElement(metadataEl, "description", metadata.Description);
        SetDcElement(metadataEl, "publisher", metadata.Publisher);

        // Replace all authors.
        metadataEl.Elements(Dc + "creator").Remove();
        foreach (var author in metadata.Authors)
        {
            metadataEl.Add(new XElement(Dc + "creator", author));
        }

        // TODO: Remove once all EPUBs have been re-indexed without legacy alexandria: meta tags.
        RemoveLegacyAlexandriaMeta(metadataEl);

        // Write series as standard EPUB3 (belongs-to-collection). Custom tags (calibre, etc.) are preserved.
        RemoveEpub3SeriesMeta(metadataEl);
        WriteEpub3SeriesMeta(metadataEl, metadata.SeriesName, metadata.SeriesIndex);

        // Stamp Alexandria UUID as dc:identifier if not already present.
        var existingAlexandriaId = metadataEl.Elements(Dc + "identifier")
            .FirstOrDefault(e => e.Value.Trim().StartsWith("alexandria:", StringComparison.OrdinalIgnoreCase));
        if (existingAlexandriaId is null)
        {
            var alexandriaUuid = metadata.AlexandriaUuid ?? metadata.GenerateDeterministicUuid();
            metadataEl.Add(new XElement(Dc + "identifier", $"alexandria:{alexandriaUuid}"));
        }

        ReplaceOpfEntry(zip, opfPath, opfDoc);
    }

    public void EmbedCover(string epubPath, byte[] coverData, string mediaType)
    {
        if (!File.Exists(epubPath))
        {
            throw new FileNotFoundException("EPUB file not found.", epubPath);
        }

        var extension = mediaType switch
        {
            "image/jpeg" => ".jpeg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            _ => ".jpg"
        };

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
        {
            opfDoc = XDocument.Load(opfStream);
        }

        var metadataEl = opfDoc.Descendants(Opf + "metadata").FirstOrDefault()
            ?? throw new InvalidOperationException("No <metadata> element found in OPF.");
        var manifest = opfDoc.Descendants(Opf + "manifest").FirstOrDefault()
            ?? throw new InvalidOperationException("No <manifest> element found in OPF.");

        var opfDirectory = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;

        // Find existing cover href and remove old entry from ZIP.
        var existingHref = FindCoverHref(opfDoc, metadataEl);
        string coverHref;
        if (existingHref is not null)
        {
            var oldEntryPath = string.IsNullOrEmpty(opfDirectory) ? existingHref : $"{opfDirectory}/{existingHref}";
            var oldEntry = zip.GetEntry(oldEntryPath.Replace('\\', '/'));
            oldEntry?.Delete();

            coverHref = existingHref;
        }
        else
        {
            coverHref = $"Images/cover{extension}";
        }

        // Add the new cover image to the ZIP.
        var newEntryPath = string.IsNullOrEmpty(opfDirectory) ? coverHref : $"{opfDirectory}/{coverHref}";
        var newCoverEntry = zip.CreateEntry(newEntryPath.Replace('\\', '/'), CompressionLevel.NoCompression);
        using (var coverStream = newCoverEntry.Open())
        {
            coverStream.Write(coverData, 0, coverData.Length);
        }

        // Update or add manifest item.
        var items = manifest.Elements(Opf + "item").ToList();
        var coverId = FindMetaByName(metadataEl, "cover");
        var coverItem = coverId is not null
            ? items.FirstOrDefault(i => string.Equals(i.Attribute("id")?.Value, coverId, StringComparison.OrdinalIgnoreCase))
            : items.FirstOrDefault(i => (i.Attribute("properties")?.Value ?? string.Empty).Split(' ').Any(p => string.Equals(p, "cover-image", StringComparison.OrdinalIgnoreCase)));

        if (coverItem is not null)
        {
            coverItem.SetAttributeValue("href", coverHref);
            coverItem.SetAttributeValue("media-type", mediaType);
        }
        else
        {
            manifest.Add(new XElement(Opf + "item", new XAttribute("id", "cover-image"), new XAttribute("href", coverHref), new XAttribute("media-type", mediaType), new XAttribute("properties", "cover-image")));
            SetMetaByName(metadataEl, "cover", "cover-image");
        }

        // Ensure a cover XHTML page exists and is first in the spine.
        // Kepubify checks if the first spine item's filename contains "cover" or "title".
        // Without this, kepubify inserts a "Page intentionally left blank" dummy page.
        EnsureCoverPage(zip, opfDoc, manifest, opfDirectory, coverHref);

        ReplaceOpfEntry(zip, opfPath, opfDoc);
    }

    public void RemoveCover(string epubPath)
    {
        if (!File.Exists(epubPath))
        {
            throw new FileNotFoundException("EPUB file not found.", epubPath);
        }

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);

        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)
            ?? throw new InvalidOperationException($"OPF file '{opfPath}' not found in archive.");

        XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
        {
            opfDoc = XDocument.Load(opfStream);
        }

        var metadataEl = opfDoc.Descendants(Opf + "metadata").FirstOrDefault()
            ?? throw new InvalidOperationException("No <metadata> element found in OPF.");
        var manifest = opfDoc.Descendants(Opf + "manifest").FirstOrDefault()
            ?? throw new InvalidOperationException("No <manifest> element found in OPF.");
        var spine = opfDoc.Descendants(Opf + "spine").FirstOrDefault();
        var opfDirectory = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        var items = manifest.Elements(Opf + "item").ToList();

        // Remove cover image from ZIP and manifest.
        var coverHref = FindCoverHref(opfDoc, metadataEl);
        if (coverHref is not null)
        {
            var coverEntryPath = string.IsNullOrEmpty(opfDirectory) ? coverHref : $"{opfDirectory}/{coverHref}";
            zip.GetEntry(coverEntryPath.Replace('\\', '/'))?.Delete();

            var coverId = FindMetaByName(metadataEl, "cover");
            var coverItem = coverId is not null
                ? items.FirstOrDefault(i => string.Equals(i.Attribute("id")?.Value, coverId, StringComparison.OrdinalIgnoreCase))
                : items.FirstOrDefault(i => (i.Attribute("properties")?.Value ?? string.Empty).Split(' ').Any(p => string.Equals(p, "cover-image", StringComparison.OrdinalIgnoreCase)));
            coverItem?.Remove();
        }

        // Remove <meta name="cover" .../> from metadata.
        SetMetaByName(metadataEl, "cover", null);

        // Remove cover XHTML page from manifest, spine, and ZIP.
        var coverPageItem = items.FirstOrDefault(i => string.Equals(i.Attribute("id")?.Value, "cover-page", StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i =>
                (i.Attribute("media-type")?.Value ?? "").StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase) &&
                (i.Attribute("href")?.Value ?? "").Contains("cover", StringComparison.OrdinalIgnoreCase));

        if (coverPageItem is not null)
        {
            var pageId = coverPageItem.Attribute("id")?.Value;
            var pageHref = coverPageItem.Attribute("href")?.Value;

            if (pageHref is not null)
            {
                var pagePath = string.IsNullOrEmpty(opfDirectory) ? pageHref : $"{opfDirectory}/{pageHref}";
                zip.GetEntry(pagePath.Replace('\\', '/'))?.Delete();
            }

            coverPageItem.Remove();

            if (spine is not null && pageId is not null)
            {
                spine.Elements(Opf + "itemref").FirstOrDefault(r => string.Equals(r.Attribute("idref")?.Value, pageId, StringComparison.OrdinalIgnoreCase))?.Remove();
            }
        }

        ReplaceOpfEntry(zip, opfPath, opfDoc);
    }

    private static void EnsureCoverPage(ZipArchive zip, XDocument opfDoc, XElement manifest, string opfDirectory, string coverImageHref)
    {
        var items = manifest.Elements(Opf + "item").ToList();

        // If the first spine item already has "cover" or "title" in its href, kepubify is satisfied.
        var spine = opfDoc.Descendants(Opf + "spine").FirstOrDefault();
        if (spine is not null)
        {
            var firstRef = spine.Elements(Opf + "itemref").FirstOrDefault();
            var firstIdRef = firstRef?.Attribute("idref")?.Value;
            if (firstIdRef is not null)
            {
                var firstItem = items.FirstOrDefault(i => string.Equals(i.Attribute("id")?.Value, firstIdRef, StringComparison.OrdinalIgnoreCase));
                var firstHref = firstItem?.Attribute("href")?.Value;
                if (firstHref is not null && (firstHref.Contains("cover", StringComparison.OrdinalIgnoreCase) || firstHref.Contains("title", StringComparison.OrdinalIgnoreCase)))
                    return;
            }
        }

        // Find existing cover page: look for id="cover-page" or an XHTML item whose href contains "cover".
        var coverPageItem = items.FirstOrDefault(i => string.Equals(i.Attribute("id")?.Value, "cover-page", StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i =>
                (i.Attribute("media-type")?.Value ?? "").StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase) &&
                (i.Attribute("href")?.Value ?? "").Contains("cover", StringComparison.OrdinalIgnoreCase));

        string coverPageId;
        string coverPageHref;

        if (coverPageItem is not null)
        {
            coverPageId = coverPageItem.Attribute("id")?.Value ?? "cover-page";
            coverPageHref = coverPageItem.Attribute("href")?.Value ?? "cover.xhtml";

            // Delete old cover page from ZIP and recreate with updated image reference.
            var oldPagePath = string.IsNullOrEmpty(opfDirectory) ? coverPageHref : $"{opfDirectory}/{coverPageHref}";
            zip.GetEntry(oldPagePath.Replace('\\', '/'))?.Delete();
        }
        else
        {
            coverPageId = "cover-page";
            coverPageHref = "cover.xhtml";
            manifest.Add(new XElement(Opf + "item", new XAttribute("id", coverPageId), new XAttribute("href", coverPageHref), new XAttribute("media-type", "application/xhtml+xml")));
        }

        // Write the cover XHTML page.
        var coverXhtml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>Cover</title>\n<style>body{{margin:0;padding:0;text-align:center;}}img{{max-width:100%;max-height:100%;}}</style>\n</head>\n<body><img src=\"{coverImageHref}\" alt=\"Cover\"/></body>\n</html>";
        var coverPagePath = string.IsNullOrEmpty(opfDirectory) ? coverPageHref : $"{opfDirectory}/{coverPageHref}";
        var pageEntry = zip.CreateEntry(coverPagePath.Replace('\\', '/'));
        using (var pageStream = pageEntry.Open())
        using (var writer = new StreamWriter(pageStream, System.Text.Encoding.UTF8))
        {
            writer.Write(coverXhtml);
        }

        // Ensure cover page is the first item in the spine.
        spine ??= opfDoc.Descendants(Opf + "spine").FirstOrDefault();
        if (spine is null)
        {
            return;
        }

        var existingRef = spine.Elements(Opf + "itemref").FirstOrDefault(r => string.Equals(r.Attribute("idref")?.Value, coverPageId, StringComparison.OrdinalIgnoreCase));
        if (existingRef is not null)
        {
            if (existingRef != spine.Elements(Opf + "itemref").First())
            {
                existingRef.Remove();
                spine.AddFirst(new XElement(Opf + "itemref", new XAttribute("idref", coverPageId)));
            }
        }
        else
        {
            spine.AddFirst(new XElement(Opf + "itemref", new XAttribute("idref", coverPageId)));
        }
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
                .ToList(),
            Identifiers = metadataEl
                .Elements(Dc + "identifier")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList(),
            Subjects = metadataEl
                .Elements(Dc + "subject")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList(),
            Contributors = metadataEl
                .Elements(Dc + "contributor")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList(),
            Rights = metadataEl.Elements(Dc + "rights").FirstOrDefault()?.Value.Trim(),
            Source = metadataEl.Elements(Dc + "source").FirstOrDefault()?.Value.Trim(),
            Type = metadataEl.Elements(Dc + "type").FirstOrDefault()?.Value.Trim(),
            Relation = metadataEl.Elements(Dc + "relation").FirstOrDefault()?.Value.Trim(),
            Coverage = metadataEl.Elements(Dc + "coverage").FirstOrDefault()?.Value.Trim(),
            Format = metadataEl.Elements(Dc + "format").FirstOrDefault()?.Value.Trim()
        };

        ParseSeriesAndCustomProperties(metadataEl, result);

        // Read Alexandria UUID from dc:identifier (format: "alexandria:{guid}").
        var alexandriaId = metadataEl.Elements(Dc + "identifier")
            .Select(e => e.Value.Trim())
            .FirstOrDefault(v => v.StartsWith("alexandria:", StringComparison.OrdinalIgnoreCase));
        if (alexandriaId is not null && Guid.TryParse(alexandriaId.AsSpan("alexandria:".Length), out var parsedUuid) && parsedUuid != Guid.Empty)
        {
            result.AlexandriaUuid = parsedUuid;
        }

        return (result, metadataEl);
    }

    /// <summary>
    /// Reads series using priority: EPUB3 standard > calibre fallback.
    /// </summary>
    private static void ParseSeriesAndCustomProperties(XElement metadataEl, EpubMetadata result)
    {
        // ── Strategy 1: EPUB3 standard (belongs-to-collection) ──
        ParseEpub3Series(metadataEl, result);
        if (!string.IsNullOrWhiteSpace(result.SeriesName))
            return;

        // ── Strategy 2: Calibre meta tags (fallback) ──
        var calibreSeries = FindMetaByName(metadataEl, "calibre:series");
        if (!string.IsNullOrWhiteSpace(calibreSeries))
        {
            result.SeriesName = calibreSeries;
            var indexStr = FindMetaByName(metadataEl, "calibre:series_index");
            if (decimal.TryParse(indexStr, System.Globalization.CultureInfo.InvariantCulture, out var idx))
                result.SeriesIndex = idx;
        }
    }

    private static void ParseEpub3Series(XElement metadataEl, EpubMetadata result)
    {
        var collectionMeta = MetaElements(metadataEl)
            .FirstOrDefault(m =>
                string.Equals(
                    m.Attribute("property")?.Value,
                    "belongs-to-collection",
                    StringComparison.OrdinalIgnoreCase));

        if (collectionMeta is null)
            return;

        var collectionName = collectionMeta.Value.Trim();
        if (string.IsNullOrWhiteSpace(collectionName))
            return;

        result.SeriesName = collectionName;

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

        if (positionMeta is not null && decimal.TryParse(positionMeta.Value.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var pos))
            result.SeriesIndex = pos;
    }

    private static string? FindMetaByName(XElement metadataEl, string name)
    {
        return MetaElements(metadataEl)
            .FirstOrDefault(m =>
                string.Equals(m.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")?.Value.Trim();
    }

    private static string? WriteCoverToDisk(ZipArchive zip, XDocument opfDoc, string opfPath, string outputDirectory, XElement metadataEl, Guid uuid)
    {
        var coverHref = FindCoverHref(opfDoc, metadataEl);
        if (coverHref is null)
            return null;

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
            return null;

        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(coverEntry.FullName);
        if (string.IsNullOrEmpty(extension))
            extension = ".jpg";

        var outputPath = Path.Combine(outputDirectory, $"{uuid}{extension}");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var entryStream = coverEntry.Open();
        using var fileStream = File.Create(outputPath);
        entryStream.CopyTo(fileStream);

        return outputPath;
    }

    private static void SetDcElement(XElement metadataEl, string localName, string? value)
    {
        metadataEl.Elements(Dc + localName).Remove();
        if (!string.IsNullOrEmpty(value))
        {
            metadataEl.Add(new XElement(Dc + localName, value));
        }
    }

    private static void SetMetaByName(XElement metadataEl, string name, string? value)
    {
        var existing = MetaElements(metadataEl).Where(m => string.Equals(m.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var el in existing)
        {
            el.Remove();
        }
        if (!string.IsNullOrEmpty(value))
        {
            metadataEl.Add(new XElement(Opf + "meta", new XAttribute("name", name), new XAttribute("content", value)));
        }
    }

    /// <summary>
    /// Removes legacy alexandria: meta tags from before the switch to dc:identifier and EPUB3 series.
    /// TODO: Remove this method once all EPUBs have been re-indexed.
    /// </summary>
    private static void RemoveLegacyAlexandriaMeta(XElement metadataEl)
    {
        string[] legacyNames = ["alexandria:uuid", "alexandria:series", "alexandria:series_index", "alexandria:series_total"];
        foreach (var name in legacyNames)
        {
            MetaElements(metadataEl)
                .Where(m => string.Equals(m.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(m => m.Remove());
        }
    }

    private static void WriteEpub3SeriesMeta(XElement metadataEl, string? seriesName, decimal? seriesIndex)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
            return;

        var collectionEl = new XElement(Opf + "meta", seriesName);
        collectionEl.SetAttributeValue("property", "belongs-to-collection");
        collectionEl.SetAttributeValue("id", "series01");
        metadataEl.Add(collectionEl);

        var typeEl = new XElement(Opf + "meta", "series");
        typeEl.SetAttributeValue("property", "collection-type");
        typeEl.SetAttributeValue("refines", "#series01");
        metadataEl.Add(typeEl);

        if (seriesIndex is not null)
        {
            var posEl = new XElement(Opf + "meta", seriesIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            posEl.SetAttributeValue("property", "group-position");
            posEl.SetAttributeValue("refines", "#series01");
            metadataEl.Add(posEl);
        }
    }

    private static void RemoveEpub3SeriesMeta(XElement metadataEl)
    {
        var collectionMetas = MetaElements(metadataEl).Where(m => string.Equals(m.Attribute("property")?.Value, "belongs-to-collection", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var meta in collectionMetas)
        {
            var refineId = meta.Attribute("id")?.Value;
            if (refineId is not null)
            {
                // Remove associated group-position and collection-type refines.
                MetaElements(metadataEl).Where(m => m.Attribute("refines")?.Value == $"#{refineId}").ToList().ForEach(m => m.Remove());
            }
            meta.Remove();
        }
    }

    private static void ReplaceOpfEntry(ZipArchive zip, string opfPath, XDocument opfDoc)
    {
        zip.GetEntry(opfPath)?.Delete();
        var newEntry = zip.CreateEntry(opfPath);
        using var stream = newEntry.Open();
        opfDoc.Save(stream);
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
