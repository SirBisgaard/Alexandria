using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class EpubMetadataTests
{
    private string tempDir = null!;
    private string epubPath = null!;
    private EpubFileHandler handler = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        GuidExtensions.SetNamespace(Guid.NewGuid());

        epubPath = Fixtures.CopyEpubTo(tempDir);
        handler = new EpubFileHandler();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void EpubMetadata_Extract_ReturnsKnownFixtureValues()
    {
        var (book, _) = handler.Extract(epubPath);

        Assert.AreEqual("Peter and Wendy", book.Title);
        Assert.HasCount(1, book.Authors);
        Assert.AreEqual("J. M. Barrie", book.Authors[0].Name);
        Assert.AreEqual("en-GB", book.Language);
        Assert.AreEqual("Standard Ebooks", book.Publisher);
        Assert.IsNotNull(book.PublicationDate);
        Assert.StartsWith("2017-11-22", book.PublicationDate);
        Assert.Contains("Fantasy literature", book.Subjects);
        Assert.Contains("Pirates -- Fiction", book.Subjects);
        Assert.IsNotNull(book.Series);
        Assert.AreEqual("The Telegraph\u2019s Greatest Villains in Literature", book.Series.Name);
        Assert.AreEqual(40m, book.Series.Index);
    }

    [TestMethod]
    public void EpubMetadata_Extract_ReturnsCoverBytes()
    {
        var (_, coverBytes) = handler.Extract(epubPath);

        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes), "Cover bytes should be a valid JPEG or PNG image.");
    }

    [TestMethod]
    public void EpubMetadata_ExtractMetadata_ReturnsKnownFixtureValues()
    {
        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("Peter and Wendy", book.Title);
        Assert.AreEqual("J. M. Barrie", book.Authors[0].Name);
        Assert.AreEqual("Standard Ebooks", book.Publisher);
        Assert.IsNotNull(book.Series);
    }

    [TestMethod]
    public void EpubMetadata_ExtractCoverBytes_ReturnsCover()
    {
        var coverBytes = handler.ExtractCoverBytes(epubPath);

        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes));
    }

    [TestMethod]
    public void EpubMetadata_ExtractAll_WritesCoverToDisk()
    {
        var coverOutputDir = Path.Combine(tempDir, "covers");
        var (_, coverFilePath) = handler.ExtractAll(epubPath, coverOutputDir, Guid.NewGuid());

        Assert.IsNotNull(coverFilePath);
        Assert.IsTrue(File.Exists(coverFilePath), "Cover file should have been written.");
        Assert.IsTrue(coverFilePath.StartsWith(coverOutputDir), "Cover file should live inside the output directory.");
        Assert.IsGreaterThan(0, new FileInfo(coverFilePath).Length);
    }

    /// <summary>
    /// Writes every Book field the EPUB handler supports and reads them back. Fields intentionally
    /// not asserted because the EPUB handler does not round-trip them: Subtitle, Isbn, Identifiers,
    /// Subjects, Rights, Source, Type, Relation, Coverage, Format. These stay at whatever the
    /// source EPUB carried; they are read on Extract but not updated on Embed.
    /// </summary>
    [TestMethod]
    public void EpubMetadata_EmbedMetadata_RoundTripsAllSupportedFields()
    {
        var (book, _) = handler.Extract(epubPath);
        book.Title = "Updated Title";
        book.Language = "da";
        book.PublicationDate = "2024-06-15";
        book.Description = "A fresh description for testing.";
        book.Publisher = "Alexandria Press";
        book.Authors =
        [
            new Author { Name = "First Author" },
            new Author { Name = "Second Author" }
        ];
        book.Contributors =
        [
            new Contributor { Name = "The Narrator", Role = "Narrator" },
            new Contributor { Name = "The Translator", Role = "Translator" }
        ];
        book.Series = new Series { Name = "Alexandria Villains", Index = 7m };

        handler.EmbedMetadata(epubPath, book);

        var (reread, _) = handler.Extract(epubPath);
        Assert.AreEqual("Updated Title", reread.Title);
        Assert.AreEqual("da", reread.Language);
        Assert.AreEqual("2024-06-15", reread.PublicationDate);
        Assert.AreEqual("A fresh description for testing.", reread.Description);
        Assert.AreEqual("Alexandria Press", reread.Publisher);
        Assert.HasCount(2, reread.Authors);
        Assert.AreEqual("First Author", reread.Authors[0].Name);
        Assert.AreEqual("Second Author", reread.Authors[1].Name);
        Assert.HasCount(2, reread.Contributors);
        var narrator = reread.Contributors.Single(c => c.Role == "Narrator");
        var translator = reread.Contributors.Single(c => c.Role == "Translator");
        Assert.AreEqual("The Narrator", narrator.Name);
        Assert.AreEqual("The Translator", translator.Name);
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("Alexandria Villains", reread.Series.Name);
        Assert.AreEqual(7m, reread.Series.Index);
    }

    [TestMethod]
    public void EpubMetadata_Contributors_UnrecognizedRole_IsFilteredOut()
    {
        var (book, _) = handler.Extract(epubPath);
        book.Contributors =
        [
            new Contributor { Name = "Valid Narrator", Role = "Narrator" },
            new Contributor { Name = "Gibberish", Role = "NotARealRole" }
        ];

        handler.EmbedMetadata(epubPath, book);

        var (reread, _) = handler.Extract(epubPath);
        Assert.HasCount(1, reread.Contributors);
        Assert.AreEqual("Valid Narrator", reread.Contributors[0].Name);
        Assert.AreEqual("Narrator", reread.Contributors[0].Role);
    }

    [TestMethod]
    public void EpubMetadata_Series_FallsBackToCalibreTagsWhenNoEpub3Series()
    {
        RewriteSeriesAsCalibre(epubPath, seriesName: "Calibre Only Series", seriesIndex: "5");

        var book = handler.ExtractMetadata(epubPath);

        Assert.IsNotNull(book.Series);
        Assert.AreEqual("Calibre Only Series", book.Series.Name);
        Assert.AreEqual(5m, book.Series.Index);
    }

    [TestMethod]
    public void EpubMetadata_EmbedCover_ReplacesCover()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        var newCoverBytes = File.ReadAllBytes(coverPath);

        handler.EmbedCover(epubPath, newCoverBytes, "image/jpeg");

        var (_, coverBytes) = handler.Extract(epubPath);
        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes), "Re-extracted cover should be a valid image.");
        Assert.IsGreaterThan(1024, coverBytes.Length);
    }

    [TestMethod]
    public void EpubMetadata_EmbedMetadata_UnchangedBook_LeavesFileUntouched()
    {
        var (book, _) = handler.Extract(epubPath);
        var hashBefore = ComputeSha256(epubPath);

        handler.EmbedMetadata(epubPath, book);

        var hashAfter = ComputeSha256(epubPath);
        CollectionAssert.AreEqual(hashBefore, hashAfter);
    }

    [TestMethod]
    public void EpubMetadata_EmbedMetadata_ChangedBook_RewritesFile()
    {
        var (book, _) = handler.Extract(epubPath);
        var hashBefore = ComputeSha256(epubPath);

        book.Title = "Different Title";
        handler.EmbedMetadata(epubPath, book);

        var hashAfter = ComputeSha256(epubPath);
        CollectionAssert.AreNotEqual(hashBefore, hashAfter);
    }

    [TestMethod]
    public void EpubMetadata_Series_WriteUsesEpub3StandardNotCalibre()
    {
        var (book, _) = handler.Extract(epubPath);
        book.Series = new Series { Name = "Alexandria Series", Index = 2m };

        handler.EmbedMetadata(epubPath, book);

        var metadataEl = ReadOpfMetadata(epubPath);
        XNamespace opf = "http://www.idpf.org/2007/opf";
        var collectionMetas = metadataEl.Elements(opf + "meta")
            .Where(m => (string?)m.Attribute("property") == "belongs-to-collection")
            .ToList();
        Assert.HasCount(1, collectionMetas);
        Assert.AreEqual("Alexandria Series", collectionMetas[0].Value.Trim());

        var calibreSeriesMeta = metadataEl.Elements(opf + "meta")
            .FirstOrDefault(m => (string?)m.Attribute("name") == "calibre:series");
        Assert.IsNull(calibreSeriesMeta, "Writer should not emit calibre:series tags.");
    }

    [TestMethod]
    public void EpubMetadata_Series_MultipleBelongsToCollection_PrefersCollectionTypeSeries()
    {
        AddSecondBelongsToCollection(epubPath, boxSetName: "Greatest Box Set", seriesName: "Real Series", seriesIndex: "9");

        var book = handler.ExtractMetadata(epubPath);

        Assert.IsNotNull(book.Series);
        Assert.AreEqual("Real Series", book.Series.Name);
        Assert.AreEqual(9m, book.Series.Index);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_Isbn13PresentAlongsidePrefixedIsbn10_PrefersIsbn13()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "9780000000001",
            "urn:isbn:0000000002"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9780000000001", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_BareDigitsUsedWhenNoPrefixedIdentifier()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "urn:some-other-scheme:not-an-isbn",
            "9781234567897"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9781234567897", book.Isbn);
    }

    // The identifier-soup fixtures below mirror real EPUBs observed in a live library —
    // calibre / uuid / goodreads / google / amazon noise mixed with one or more ISBN forms.
    // Values are anonymized but preserve the structure and casing variety of the originals.

    [TestMethod]
    public void EpubMetadata_Isbn_CalibreStyleIdentifierSoup_ReturnsIsbn13()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "9780000000001",
            "ISBN:9780000000001",
            "calibre:00000000-0000-0000-0000-000000000001",
            "uuid:00000000-0000-0000-0000-000000000002"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9780000000001", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_Isbn13AndIsbn10AmongMixedIdentifiers_ReturnsIsbn13()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "0000000001",
            "99999999",
            "9780000000002",
            "00000000-0000-0000-0000-000000000003",
            "AAAAAAAAAAAA",
            "urn:amazon:0000000001",
            "urn:goodreads:99999999",
            "urn:google:AAAAAAAAAAAA",
            "urn:isbn:0000000003",
            "urn:isbn:9780000000002",
            "AAAAAAAAAAAB"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9780000000002", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_OnlyIsbn10AvailableAcrossFormats_ReturnsIsbn10()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "urn:amazon:0000000001",
            "B000000000",
            "MOBI-ASIN:B000000001",
            "0000000001"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("0000000001", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_NoIsbnCandidatesInIdentifiers_ReturnsNull()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "calibre:00000000-0000-0000-0000-000000000001",
            "uuid:00000000-0000-0000-0000-000000000002",
            "urn:uuid:00000000-0000-0000-0000-000000000003",
            "GOOGLE:AAAAAAAAAAAA",
            "GOODREADS:99999999",
            "urn:amazon:B000000000",
            "MOBI-ASIN:B000000001"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.IsNull(book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_LowercaseIsbnPrefix_Recognized()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "calibre:00000000-0000-0000-0000-000000000001",
            "google:AAAAAAAAAAAA",
            "isbn:9780000000004",
            "uuid:00000000-0000-0000-0000-000000000004"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9780000000004", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_MalformedCompoundIdentifiers_SkippedInFavorOfValidIsbn13()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "URNISBN/9780000000005:9780000000005",
            "{00000000-0000-0000-0000-000000000005}",
            "MOBI-ASIN:B000000005",
            "AMAZON:0000000005",
            "9780000000005"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9780000000005", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_HyphenatedIsbn13_StrippedAndReturned()
    {
        // Example from isbn-13.info — hyphenated 13-digit ISBN normalizes to digits only.
        RewriteIdentifiers(epubPath, identifiers: ["urn:isbn:978-1-56619-909-4"]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9781566199094", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_HyphenatedIsbn10_StrippedAndReturned()
    {
        // Example from isbn-13.info — ISBN-10 with hyphens in the bare form.
        RewriteIdentifiers(epubPath, identifiers: ["1-56619-909-3"]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("1566199093", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_Isbn10WithXCheckDigit_AcceptedAndUppercased()
    {
        // ISBN-10 allows 'X' as the check digit (value 10). Case-insensitive input normalizes to upper.
        RewriteIdentifiers(epubPath, identifiers: ["0-306-40615-x"]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("030640615X", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_Isbn_InvalidPrefixed_FallsThroughToValidBare()
    {
        RewriteIdentifiers(epubPath, identifiers:
        [
            "ISBN:not-a-real-isbn",
            "9781566199094"
        ]);

        var book = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("9781566199094", book.Isbn);
    }

    [TestMethod]
    public void EpubMetadata_EmbedCover_InsertsCoverPageAsFirstSpineItem()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        handler.EmbedCover(epubPath, File.ReadAllBytes(coverPath), "image/jpeg");

        var firstHref = GetFirstSpineItemHref(epubPath);
        Assert.IsNotNull(firstHref);
        Assert.IsTrue(
            firstHref.Contains("cover", StringComparison.OrdinalIgnoreCase) || firstHref.Contains("title", StringComparison.OrdinalIgnoreCase),
            $"First spine item href '{firstHref}' should contain 'cover' or 'title' (required by kepubify).");
    }

    [TestMethod]
    public void EpubMetadata_EmbedCover_StoresCoverImageWithoutCompression()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        handler.EmbedCover(epubPath, File.ReadAllBytes(coverPath), "image/jpeg");

        using var stream = File.OpenRead(epubPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var coverHref = GetCoverHref(epubPath)!;
        var coverEntry = zip.Entries.Single(e => e.FullName.EndsWith(coverHref, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(coverEntry.Length, coverEntry.CompressedLength);
    }

    [TestMethod]
    public void EpubMetadata_WordCount_SkipsScriptStyleAndHeadContent()
    {
        var baseline = handler.ExtractMetadata(epubPath).File.WordCount;
        Assert.IsNotNull(baseline);

        InjectScriptAndStyleIntoFirstXhtml(epubPath, wordsPerTag: 500);

        var afterInjection = handler.ExtractMetadata(epubPath).File.WordCount;
        Assert.AreEqual(baseline, afterInjection);
    }

    [TestMethod]
    public void EpubMetadata_RemoveCover_LeavesNoCover()
    {
        handler.RemoveCover(epubPath);

        var (_, coverBytes) = handler.Extract(epubPath);
        Assert.IsNull(coverBytes);
    }

    private static bool IsJpegOrPng(byte[] data)
    {
        if (data.Length < 8)
            return false;
        if (data[0] == 0xFF && data[1] == 0xD8)
            return true;
        return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    }

    private static byte[] ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return SHA256.HashData(stream);
    }

    private static string GetOpfPath(ZipArchive zip)
    {
        XNamespace container = "urn:oasis:names:tc:opendocument:xmlns:container";
        using var cs = zip.GetEntry("META-INF/container.xml")!.Open();
        var cdoc = XDocument.Load(cs);
        return cdoc.Descendants(container + "rootfile").First().Attribute("full-path")!.Value;
    }

    private static XElement ReadOpfMetadata(string epubPath)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        using var stream = File.OpenRead(epubPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var opfPath = GetOpfPath(zip);
        using var os = zip.GetEntry(opfPath)!.Open();
        return XDocument.Load(os).Descendants(opf + "metadata").Single();
    }

    private static string? GetFirstSpineItemHref(string epubPath)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        using var stream = File.OpenRead(epubPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var opfPath = GetOpfPath(zip);
        using var os = zip.GetEntry(opfPath)!.Open();
        var doc = XDocument.Load(os);
        var spine = doc.Descendants(opf + "spine").Single();
        var firstIdRef = spine.Elements(opf + "itemref").First().Attribute("idref")!.Value;
        var manifest = doc.Descendants(opf + "manifest").Single();
        return manifest.Elements(opf + "item")
            .FirstOrDefault(i => string.Equals((string?)i.Attribute("id"), firstIdRef, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("href")?.Value;
    }

    private static string? GetCoverHref(string epubPath)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        using var stream = File.OpenRead(epubPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var opfPath = GetOpfPath(zip);
        using var os = zip.GetEntry(opfPath)!.Open();
        var doc = XDocument.Load(os);
        var manifest = doc.Descendants(opf + "manifest").Single();
        var coverItem = manifest.Elements(opf + "item")
            .FirstOrDefault(i => ((string?)i.Attribute("properties") ?? "").Split(' ').Any(p => string.Equals(p, "cover-image", StringComparison.OrdinalIgnoreCase)));
        return coverItem?.Attribute("href")?.Value;
    }

    /// <summary>
    /// Appends a second &lt;meta property="belongs-to-collection"&gt; (a box-set collection) ahead of the
    /// existing series collection, without a collection-type refinement. The series-type collection is
    /// rewritten with a fresh id and its <paramref name="seriesName"/> / <paramref name="seriesIndex"/>.
    /// Used to verify the handler prefers the collection-type=series entry over the untyped box-set.
    /// </summary>
    private static void AddSecondBelongsToCollection(string epubPath, string boxSetName, string seriesName, string seriesIndex)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);
        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)!;

        XDocument opfDoc;
        using (var os = opfEntry.Open())
            opfDoc = XDocument.Load(os);

        var metadataEl = opfDoc.Descendants(opf + "metadata").Single();

        foreach (var m in metadataEl.Elements(opf + "meta").Where(m => (string?)m.Attribute("property") == "belongs-to-collection").ToList())
        {
            var id = (string?)m.Attribute("id");
            if (id is not null)
            {
                foreach (var refined in metadataEl.Elements(opf + "meta").Where(r => (string?)r.Attribute("refines") == $"#{id}").ToList())
                    refined.Remove();
            }
            m.Remove();
        }

        var boxSetEl = new XElement(opf + "meta", boxSetName,
            new XAttribute("property", "belongs-to-collection"),
            new XAttribute("id", "boxset01"));
        metadataEl.Add(boxSetEl);

        var seriesEl = new XElement(opf + "meta", seriesName,
            new XAttribute("property", "belongs-to-collection"),
            new XAttribute("id", "series02"));
        metadataEl.Add(seriesEl);
        metadataEl.Add(new XElement(opf + "meta", "series",
            new XAttribute("property", "collection-type"),
            new XAttribute("refines", "#series02")));
        metadataEl.Add(new XElement(opf + "meta", seriesIndex,
            new XAttribute("property", "group-position"),
            new XAttribute("refines", "#series02")));

        opfEntry.Delete();
        using var w = zip.CreateEntry(opfPath).Open();
        opfDoc.Save(w);
    }

    /// <summary>
    /// Replaces all dc:identifier elements in the OPF with the supplied list (in order). Lets the test
    /// stage ISBN candidates of each prefix form so the handler's extraction priority can be observed.
    /// </summary>
    private static void RewriteIdentifiers(string epubPath, IEnumerable<string> identifiers)
    {
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace opf = "http://www.idpf.org/2007/opf";
        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);
        var opfPath = GetOpfPath(zip);
        var opfEntry = zip.GetEntry(opfPath)!;

        XDocument opfDoc;
        using (var os = opfEntry.Open())
            opfDoc = XDocument.Load(os);

        var metadataEl = opfDoc.Descendants(opf + "metadata").Single();
        foreach (var id in metadataEl.Elements(dc + "identifier").ToList())
            id.Remove();
        foreach (var value in identifiers)
            metadataEl.Add(new XElement(dc + "identifier", value));

        opfEntry.Delete();
        using var w = zip.CreateEntry(opfPath).Open();
        opfDoc.Save(w);
    }

    /// <summary>
    /// Injects a large &lt;script&gt; block and a &lt;style&gt; block plus extra &lt;title&gt; words into the head of the
    /// first XHTML content file. The word count extraction should skip all three — so the pre/post word
    /// count must match. <paramref name="wordsPerTag"/> controls how many words land in each skipped region.
    /// </summary>
    private static void InjectScriptAndStyleIntoFirstXhtml(string epubPath, int wordsPerTag)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);
        var opfPath = GetOpfPath(zip);

        XDocument opfDoc;
        using (var os = zip.GetEntry(opfPath)!.Open())
            opfDoc = XDocument.Load(os);

        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        var manifest = opfDoc.Descendants(opf + "manifest").Single();
        var firstXhtml = manifest.Elements(opf + "item")
            .First(i => ((string?)i.Attribute("media-type") ?? "").Contains("xhtml", StringComparison.OrdinalIgnoreCase));
        var href = firstXhtml.Attribute("href")!.Value;
        var entryPath = (string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}").Replace('\\', '/');
        var entry = zip.GetEntry(entryPath)!;

        XDocument xhtmlDoc;
        using (var es = entry.Open())
            xhtmlDoc = XDocument.Load(es);

        var filler = string.Join(' ', Enumerable.Range(1, wordsPerTag).Select(i => $"word{i}"));

        var head = xhtmlDoc.Root!.Element(xhtml + "head") ?? new XElement(xhtml + "head");
        if (head.Parent is null)
            xhtmlDoc.Root!.AddFirst(head);
        head.Add(new XElement(xhtml + "script", new XAttribute("type", "text/javascript"), filler));
        head.Add(new XElement(xhtml + "style", filler));
        head.Add(new XElement(xhtml + "title", filler));

        entry.Delete();
        using var w = zip.CreateEntry(entryPath).Open();
        xhtmlDoc.Save(w);
    }

    /// <summary>
    /// Replaces the EPUB3 belongs-to-collection metadata with calibre:series / calibre:series_index
    /// meta tags. Used to fabricate the "no EPUB3 series, only calibre tags" state since
    /// <see cref="EpubFileHandler.EmbedMetadata"/> only writes EPUB3-standard series.
    /// </summary>
    private static void RewriteSeriesAsCalibre(string epubPath, string seriesName, string seriesIndex)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace container = "urn:oasis:names:tc:opendocument:xmlns:container";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);

        var containerEntry = zip.GetEntry("META-INF/container.xml")!;
        string opfPath;
        using (var cs = containerEntry.Open())
        {
            var cdoc = XDocument.Load(cs);
            opfPath = cdoc.Descendants(container + "rootfile").First().Attribute("full-path")!.Value;
        }

        var opfEntry = zip.GetEntry(opfPath)!;
        XDocument opfDoc;
        using (var os = opfEntry.Open())
        {
            opfDoc = XDocument.Load(os);
        }

        var metadataEl = opfDoc.Descendants(opf + "metadata").Single();

        var epub3Collections = metadataEl.Elements(opf + "meta")
            .Where(m => (string?)m.Attribute("property") == "belongs-to-collection")
            .ToList();
        var idsToPurge = epub3Collections
            .Select(m => (string?)m.Attribute("id"))
            .Where(id => id is not null)
            .Select(id => $"#{id}")
            .ToHashSet();
        foreach (var meta in epub3Collections)
            meta.Remove();
        var refinedMetas = metadataEl.Elements(opf + "meta")
            .Where(m => (string?)m.Attribute("refines") is { } r && idsToPurge.Contains(r))
            .ToList();
        foreach (var meta in refinedMetas)
            meta.Remove();

        metadataEl.Add(new XElement(opf + "meta",
            new XAttribute("name", "calibre:series"),
            new XAttribute("content", seriesName)));
        metadataEl.Add(new XElement(opf + "meta",
            new XAttribute("name", "calibre:series_index"),
            new XAttribute("content", seriesIndex)));

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfPath);
        using var writer = newEntry.Open();
        opfDoc.Save(writer);
    }
}
