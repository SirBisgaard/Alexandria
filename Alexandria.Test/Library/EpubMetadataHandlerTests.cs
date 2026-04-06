using Alexandria.Business.Extensions;
using Alexandria.Business.Models.Files;
using Alexandria.DataAccess.Services;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class EpubMetadataHandlerTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Books");

    private string outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);
    }

    [TestMethod]
    public void Extract_DetSidsteKongerige_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.AreEqual("Det sidste kongerige", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(1, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_DenDanskeRytter_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #2 - Den danske rytter.epub");

        Assert.AreEqual("Den danske rytter", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(2, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_NordensHerrer_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #3 - Nordens herrer.epub");

        Assert.AreEqual("Nordens herrer", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(3, metadata.SeriesIndex);
    }

    [TestMethod]
    public void ExtractAll_AllBooks_ExtractsCover()
    {
        var extractor = new EpubMetadataHandler();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var metadata = extractor.ExtractAll(epub, coverDir);

            Assert.IsNotNull(metadata.CoverFilePath, $"Cover should have been extracted for {Path.GetFileName(epub)}.");
            Assert.IsTrue(File.Exists(metadata.CoverFilePath), $"Cover file should exist for {Path.GetFileName(epub)}.");
            Assert.IsGreaterThan(0, new FileInfo(metadata.CoverFilePath).Length, $"Cover file should not be empty for {Path.GetFileName(epub)}.");
        }
    }

    [TestMethod]
    public void Extract_NonExistentFile_ThrowsFileNotFoundException()
    {
        var extractor = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => extractor.ExtractAll(bogusPath, outputDir));
    }

    [TestMethod]
    public void Extract_SameBook_ProducesDeterministicUuid()
    {
        var extractor = new EpubMetadataHandler();
        var inputPath = Path.Combine(ContentBooksDir, "Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var first = extractor.ExtractAll(inputPath, outputDir);
        var second = extractor.ExtractAll(inputPath, outputDir);

        Assert.AreEqual(first.DeterministicUuid, second.DeterministicUuid, "UUID should be deterministic for the same title and authors.");
        Assert.AreNotEqual(Guid.Empty, first.DeterministicUuid);
    }

    [TestMethod]
    public void Extract_DifferentBooks_ProduceDifferentUuids()
    {
        var extractor = new EpubMetadataHandler();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");
        var uuids = new HashSet<Guid>();

        foreach (var epub in epubs)
        {
            var metadata = extractor.ExtractAll(epub, outputDir);
            Assert.IsTrue(uuids.Add(metadata.DeterministicUuid), $"UUID collision for {Path.GetFileName(epub)}.");
        }
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesTitle_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var original = handler.ExtractAll(epubPath, outputDir);

        var modified = new EpubMetadata
        {
            Title = "New Title",
            Authors = original.Authors,
            Language = original.Language,
            PublicationDate = original.PublicationDate,
            Description = original.Description,
            Publisher = original.Publisher,
            SeriesName = original.SeriesName,
            SeriesIndex = original.SeriesIndex
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.AreEqual("New Title", result.Title);
        Assert.HasCount(original.Authors.Count, result.Authors);
        Assert.AreEqual(original.SeriesName, result.SeriesName);
        Assert.AreEqual(original.SeriesIndex, result.SeriesIndex);
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesAuthors_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var original = handler.ExtractAll(epubPath, outputDir);

        var modified = new EpubMetadata
        {
            Title = original.Title,
            Authors = ["Author One", "Author Two"],
            Language = original.Language,
            SeriesName = original.SeriesName,
            SeriesIndex = original.SeriesIndex
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.HasCount(2, result.Authors);
        Assert.Contains("Author One", result.Authors);
        Assert.Contains("Author Two", result.Authors);
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesAllFields_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var modified = new EpubMetadata
        {
            Title = "Test Title",
            Authors = ["Test Author"],
            Language = "en",
            PublicationDate = "2025-01-01",
            Description = "A test description",
            Publisher = "Test Publisher",
            SeriesName = "Test Series",
            SeriesIndex = 42
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.AreEqual("Test Title", result.Title);
        Assert.HasCount(1, result.Authors);
        Assert.AreEqual("Test Author", result.Authors[0]);
        Assert.AreEqual("en", result.Language);
        Assert.AreEqual("2025-01-01", result.PublicationDate);
        Assert.AreEqual("A test description", result.Description);
        Assert.AreEqual("Test Publisher", result.Publisher);
        Assert.AreEqual("Test Series", result.SeriesName);
        Assert.AreEqual(42, result.SeriesIndex);
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesSeries_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var original = handler.ExtractAll(epubPath, outputDir);

        var modified = new EpubMetadata
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            SeriesName = "New Series Name",
            SeriesIndex = 7
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.AreEqual("New Series Name", result.SeriesName);
        Assert.AreEqual(7, result.SeriesIndex);
    }

    [TestMethod]
    public void EmbedMetadata_ClearsSeries_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var original = handler.ExtractAll(epubPath, outputDir);
        Assert.IsNotNull(original.SeriesName, "Test book should have a series.");

        var modified = new EpubMetadata
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            SeriesName = null,
            SeriesIndex = null
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.IsNull(result.SeriesName);
        Assert.IsNull(result.SeriesIndex);
    }

    [TestMethod]
    public void EmbedMetadata_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.EmbedMetadata(bogusPath, new EpubMetadata { Title = "x", Authors = ["a"], Language = "en" }));
    }

    [TestMethod]
    public void EmbedCover_ReplacesExistingCover_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var originalCoverDir = Path.Combine(outputDir, "original-cover");
        var originalMetadata = handler.ExtractAll(epubPath, originalCoverDir);
        Assert.IsNotNull(originalMetadata.CoverFilePath);
        var originalCoverSize = new FileInfo(originalMetadata.CoverFilePath).Length;

        // Create a small but valid JPEG (minimal JFIF).
        var fakeCover = CreateMinimalJpeg();

        handler.EmbedCover(epubPath, fakeCover, "image/jpeg");

        var newCoverDir = Path.Combine(outputDir, "new-cover");
        var newMetadata = handler.ExtractAll(epubPath, newCoverDir);
        Assert.IsNotNull(newMetadata.CoverFilePath);

        var newCoverSize = new FileInfo(newMetadata.CoverFilePath).Length;
        Assert.AreEqual(fakeCover.Length, newCoverSize, "Extracted cover should match the embedded cover size.");
        Assert.AreNotEqual(originalCoverSize, newCoverSize, "Cover size should differ from original.");
    }

    [TestMethod]
    public void EmbedCover_CreatesCoverXhtmlAsFirstSpineItem()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Vasily Grossman - Life and Fate.epub");

        handler.EmbedCover(epubPath, CreateMinimalJpeg(), "image/jpeg");

        // Verify the EPUB now has a cover XHTML page as the first spine item.
        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        using var opfStream = opfEntry.Open();
        var opfDoc = System.Xml.Linq.XDocument.Load(opfStream);
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";

        // First spine itemref should point to a cover page.
        var spine = opfDoc.Descendants(opfNs + "spine").First();
        var firstItemRef = spine.Elements(opfNs + "itemref").First();
        var firstIdRef = firstItemRef.Attribute("idref")?.Value;
        Assert.IsNotNull(firstIdRef);

        // The referenced manifest item should have a href containing "cover".
        var manifest = opfDoc.Descendants(opfNs + "manifest").First();
        var coverPageItem = manifest.Elements(opfNs + "item").First(i => i.Attribute("id")?.Value == firstIdRef);
        var coverPageHref = coverPageItem.Attribute("href")?.Value;
        Assert.IsNotNull(coverPageHref);
        Assert.IsTrue(coverPageHref.Contains("cover", StringComparison.OrdinalIgnoreCase), $"Cover page href '{coverPageHref}' should contain 'cover'.");

        // The cover XHTML should exist in the ZIP and reference an image.
        var opfDir = System.IO.Path.GetDirectoryName(opfEntry.FullName)?.Replace('\\', '/') ?? "";
        var coverPagePath = string.IsNullOrEmpty(opfDir) ? coverPageHref : $"{opfDir}/{coverPageHref}";
        var coverPageEntry = zip.GetEntry(coverPagePath.Replace('\\', '/'));
        Assert.IsNotNull(coverPageEntry, "cover.xhtml should exist in the EPUB.");

        using var pageStream = coverPageEntry.Open();
        using var reader = new StreamReader(pageStream);
        var html = reader.ReadToEnd();
        Assert.Contains("<img", html, "Cover page should contain an img tag.");
    }

    [TestMethod]
    public void EmbedCover_PreservesOriginalCoverFilePath()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        handler.EmbedCover(epubPath, CreateMinimalJpeg(), "image/jpeg");

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        // The original cover was at EPUB/images/cover.jpg — it must stay there, not become cover.jpeg.
        Assert.IsNotNull(zip.GetEntry("EPUB/images/cover.jpg"), "Cover image should remain at its original path (cover.jpg, not cover.jpeg).");
        Assert.IsNull(zip.GetEntry("EPUB/images/cover.jpeg"), "No renamed cover.jpeg should exist.");

        // Manifest href should also still reference the original path.
        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        using var opfStream = opfEntry.Open();
        var opfDoc = System.Xml.Linq.XDocument.Load(opfStream);
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";
        var manifest = opfDoc.Descendants(opfNs + "manifest").First();
        var coverItem = manifest.Elements(opfNs + "item").First(i => (i.Attribute("properties")?.Value ?? "").Contains("cover-image"));
        Assert.AreEqual("images/cover.jpg", coverItem.Attribute("href")?.Value);
    }

    [TestMethod]
    public void EmbedCover_DoesNotAddDuplicateCoverPage()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Count XHTML entries before.
        int xhtmlCountBefore;
        using (var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
            xhtmlCountBefore = zip.Entries.Count(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".html"));

        handler.EmbedCover(epubPath, CreateMinimalJpeg(), "image/jpeg");

        using var stream2 = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip2 = new System.IO.Compression.ZipArchive(stream2, System.IO.Compression.ZipArchiveMode.Read);

        var xhtmlCountAfter = zip2.Entries.Count(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".html"));
        Assert.AreEqual(xhtmlCountBefore, xhtmlCountAfter, "EmbedCover should not add a new cover page when one already exists.");

        // First spine item should still be the original titlepage.
        var opfEntry = zip2.Entries.First(e => e.FullName.EndsWith(".opf"));
        using var opfStream = opfEntry.Open();
        var opfDoc = System.Xml.Linq.XDocument.Load(opfStream);
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";
        var spine = opfDoc.Descendants(opfNs + "spine").First();
        var firstIdRef = spine.Elements(opfNs + "itemref").First().Attribute("idref")?.Value;
        Assert.AreEqual("titlepage", firstIdRef, "First spine item should remain the original titlepage.");
    }

    [TestMethod]
    public void EmbedCover_ExistingCoverPageReferencesRemainValid()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        handler.EmbedCover(epubPath, CreateMinimalJpeg(), "image/jpeg");

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        // Read the titlepage.xhtml and extract the SVG image reference.
        var titlepageEntry = zip.GetEntry("EPUB/xhtml/titlepage.xhtml");
        Assert.IsNotNull(titlepageEntry, "titlepage.xhtml should still exist.");

        using var pageStream = titlepageEntry.Open();
        using var reader = new StreamReader(pageStream);
        var html = reader.ReadToEnd();

        // Extract xlink:href value from the SVG image element.
        var match = System.Text.RegularExpressions.Regex.Match(html, @"xlink:href=""([^""]+)""");
        Assert.IsTrue(match.Success, "titlepage.xhtml should contain an SVG image with xlink:href.");
        var imageRef = match.Groups[1].Value;

        // Resolve the reference relative to titlepage.xhtml's directory (EPUB/xhtml/).
        var resolvedPath = $"EPUB/xhtml/{imageRef}".Replace('\\', '/');
        // Normalize "../images/cover.jpg" → "EPUB/images/cover.jpg"
        resolvedPath = Path.GetFullPath(resolvedPath, "/").TrimStart('/');

        var imageEntry = zip.GetEntry(resolvedPath);
        Assert.IsNotNull(imageEntry, $"Image referenced by titlepage.xhtml ({imageRef} → {resolvedPath}) must exist in the EPUB.");
    }

    [TestMethod]
    public void EmbedCover_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.EmbedCover(bogusPath, [0xFF, 0xD8], "image/jpeg"));
    }

    [TestMethod]
    public void RemoveCover_RemovesCoverFromEpub()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Verify cover exists before removal.
        var beforeDir = Path.Combine(outputDir, "before-cover");
        var beforeMetadata = handler.ExtractAll(epubPath, beforeDir);
        Assert.IsNotNull(beforeMetadata.CoverFilePath, "Test book should have a cover before removal.");

        handler.RemoveCover(epubPath);

        // Verify cover is gone after removal.
        var afterDir = Path.Combine(outputDir, "after-cover");
        var afterMetadata = handler.ExtractAll(epubPath, afterDir);
        Assert.IsNull(afterMetadata.CoverFilePath, "Cover should be null after removal.");
    }

    [TestMethod]
    public void RemoveCover_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.RemoveCover(bogusPath));
    }

    private string CopyTestEpub(string fileName)
    {
        var source = Path.Combine(ContentBooksDir, fileName);
        var dest = Path.Combine(outputDir, fileName);
        File.Copy(source, dest);
        return dest;
    }

    private static byte[] CreateMinimalJpeg()
    {
        // Minimal valid JPEG: SOI + APP0 (JFIF header) + a small 1x1 image + EOI.
        // This is a real 1x1 white JPEG.
        return [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
            0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
            0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
            0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
            0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
            0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0xFF, 0xC4, 0x00, 0xB5, 0x10, 0x00, 0x02, 0x01, 0x03,
            0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 0x00, 0x01, 0x7D,
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06,
            0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
            0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72,
            0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45,
            0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75,
            0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3,
            0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
            0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9,
            0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
            0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4,
            0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01,
            0x00, 0x00, 0x3F, 0x00, 0x7B, 0x94, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xD9
        ];
    }

    [TestMethod]
    public void Extract_EpubWithEmptyGuidUuid_IgnoresItAndReturnsNull()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Directly inject Guid.Empty into the EPUB's OPF (simulates a previous bug).
        InjectAlexandriaUuid(epubPath, Guid.Empty.ToString());

        // Re-extract: the Guid.Empty should be rejected, so AlexandriaUuid comes back null.
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.IsNull(result.AlexandriaUuid, "Guid.Empty in EPUB should be treated as unset.");
        Assert.AreNotEqual(Guid.Empty, result.DeterministicUuid, "DeterministicUuid should never be Guid.Empty.");
    }

    [TestMethod]
    public void EmbedMetadata_ExistingUuid_DoesNotOverwrite()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Stamp a known UUID directly into the EPUB.
        var originalUuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        InjectAlexandriaUuid(epubPath, originalUuid.ToString());

        // Now embed metadata with a different UUID.
        var original = handler.ExtractAll(epubPath, outputDir);
        var modified = new EpubMetadata
        {
            AlexandriaUuid = Guid.NewGuid(),
            Title = "Changed Title",
            Authors = original.Authors,
            Language = original.Language,
            SeriesName = original.SeriesName,
            SeriesIndex = original.SeriesIndex
        };
        handler.EmbedMetadata(epubPath, modified);

        // The original UUID should be preserved, title should be updated.
        var result = handler.ExtractAll(epubPath, outputDir);

        Assert.AreEqual(originalUuid, result.AlexandriaUuid, "Existing UUID in EPUB should not be overwritten.");
        Assert.AreEqual("Changed Title", result.Title);
    }

    private static void InjectAlexandriaUuid(string epubPath, string uuid)
    {
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update);

        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        System.Xml.Linq.XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = System.Xml.Linq.XDocument.Load(opfStream);

        var metadataEl = opfDoc.Descendants(opfNs + "metadata").First();

        // Remove any existing alexandria:uuid meta.
        foreach (var el in metadataEl.Elements(opfNs + "meta").Where(m => m.Attribute("name")?.Value == "alexandria:uuid").ToList())
            el.Remove();

        // Add the tainted one.
        metadataEl.Add(new System.Xml.Linq.XElement(opfNs + "meta",
            new System.Xml.Linq.XAttribute("name", "alexandria:uuid"),
            new System.Xml.Linq.XAttribute("content", uuid)));

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfEntry.FullName);
        using var writeStream = newEntry.Open();
        opfDoc.Save(writeStream);
    }

    private EpubMetadata ExtractBook(string fileName)
    {
        var extractor = new EpubMetadataHandler();
        var inputPath = Path.Combine(ContentBooksDir, fileName);
        return extractor.ExtractAll(inputPath, outputDir);
    }
}
