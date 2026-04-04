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

    /// <summary>
    /// Extracts metadata from an EPUB file.
    /// </summary>
    /// <param name="epubPath">Absolute or relative path to the .epub file.</param>
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
    /// Extracts the cover image from an EPUB file to disk.
    /// </summary>
    /// <param name="epubPath">Absolute or relative path to the .epub file.</param>
    /// <param name="coverOutputDirectory">Directory where the cover image should be saved.</param>
    /// <returns>The path to the extracted cover file, or null if no cover was found.</returns>
    public string? ExtractCover(string epubPath, string coverOutputDirectory)
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

        var (metadata, metadataEl) = ParseMetadata(opfDoc);

        return WriteCoverToDisk(zip, opfDoc, opfPath, coverOutputDirectory, metadataEl, metadata.Uuid);
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

        // Update Calibre series meta tags.
        SetMetaByName(metadataEl, "calibre:series", metadata.SeriesName);
        SetMetaByName(metadataEl, "calibre:series_index", metadata.SeriesIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Remove EPUB 3 series metadata to avoid conflicts with Calibre tags.
        RemoveEpub3SeriesMeta(metadataEl);

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

            // Keep the same relative path but update the extension if needed.
            var existingWithoutExt = Path.ChangeExtension(existingHref, null);
            coverHref = existingWithoutExt + extension;
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

    private static void EnsureCoverPage(ZipArchive zip, XDocument opfDoc, XElement manifest, string opfDirectory, string coverImageHref)
    {
        var items = manifest.Elements(Opf + "item").ToList();

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
        var spine = opfDoc.Descendants(Opf + "spine").FirstOrDefault();
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
        if (!string.IsNullOrWhiteSpace(calibreSeries))
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

        var collectionName = collectionMeta.Value.Trim();
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return;
        }

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
