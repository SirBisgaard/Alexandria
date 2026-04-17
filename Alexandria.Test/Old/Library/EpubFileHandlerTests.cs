using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;

namespace Alexandria.Tests.Old.Library;

[TestClass]
[Ignore]
public sealed class EpubFileHandlerTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "Old", "TestData", "Books");

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
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.AreEqual("Det sidste kongerige", book.Title);
        Assert.Contains("Bernard Cornwell", book.Authors.Select(a => a.Name));
        Assert.AreEqual("The Last Kingdom", book.Series?.Name);
        Assert.AreEqual(1, book.Series?.Index);
    }

    [TestMethod]
    public void Extract_DenDanskeRytter_ReturnsCorrectMetadata()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #2 - Den danske rytter.epub");

        Assert.AreEqual("Den danske rytter", book.Title);
        Assert.Contains("Bernard Cornwell", book.Authors.Select(a => a.Name));
        Assert.AreEqual("The Last Kingdom", book.Series?.Name);
        Assert.AreEqual(2, book.Series?.Index);
    }

    [TestMethod]
    public void Extract_NordensHerrer_ReturnsCorrectMetadata()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #3 - Nordens herrer.epub");

        Assert.AreEqual("Nordens herrer", book.Title);
        Assert.Contains("Bernard Cornwell", book.Authors.Select(a => a.Name));
        Assert.AreEqual("The Last Kingdom", book.Series?.Name);
        Assert.AreEqual(3, book.Series?.Index);
    }

    [TestMethod]
    public void ExtractAll_AllBooks_ExtractsCover()
    {
        var extractor = new EpubFileHandler();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var (_, coverFilePath) = extractor.ExtractAll(epub, coverDir, Guid.NewGuid());

            Assert.IsNotNull(coverFilePath, $"Cover should have been extracted for {Path.GetFileName(epub)}.");
            Assert.IsTrue(File.Exists(coverFilePath), $"Cover file should exist for {Path.GetFileName(epub)}.");
            Assert.IsGreaterThan(0, new FileInfo(coverFilePath).Length, $"Cover file should not be empty for {Path.GetFileName(epub)}.");
        }
    }

    [TestMethod]
    public void Extract_NonExistentFile_ThrowsFileNotFoundException()
    {
        var extractor = new EpubFileHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => extractor.ExtractAll(bogusPath, outputDir, Guid.NewGuid()));
    }

    [TestMethod]
    public void Extract_ReturnsEmptyBookUuid()
    {
        var extractor = new EpubFileHandler();
        var inputPath = Path.Combine(ContentBooksDir, "Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var (book, _) = extractor.ExtractAll(inputPath, outputDir, Guid.NewGuid());

        Assert.AreEqual(Guid.Empty, book.BookUuid, "Extracted BookUuid should be Empty — identity comes from the database, not the file.");
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesTitle_ExtractsCorrectly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        var modified = new Book
        {
            Title = "New Title",
            Authors = original.Authors,
            Language = original.Language,
            PublicationDate = original.PublicationDate,
            Description = original.Description,
            Publisher = original.Publisher,
            Series = original.Series
        };

        handler.EmbedMetadata(epubPath, modified);
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("New Title", result.Title);
        Assert.HasCount(original.Authors.Count, result.Authors);
        Assert.AreEqual(original.Series?.Name, result.Series?.Name);
        Assert.AreEqual(original.Series?.Index, result.Series?.Index);
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesAuthors_ExtractsCorrectly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        var modified = new Book
        {
            Title = original.Title,
            Authors = [new Author { Name = "Author One" }, new Author { Name = "Author Two" }],
            Language = original.Language,
            Series = original.Series
        };

        handler.EmbedMetadata(epubPath, modified);
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.HasCount(2, result.Authors);
        Assert.Contains("Author One", result.Authors.Select(a => a.Name));
        Assert.Contains("Author Two", result.Authors.Select(a => a.Name));
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesAllFields_ExtractsCorrectly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var modified = new Book
        {
            Title = "Test Title",
            Authors = [new Author { Name = "Test Author" }],
            Language = "en",
            PublicationDate = "2025-01-01",
            Description = "A test description",
            Publisher = "Test Publisher",
            Series = new Series { Name = "Test Series", Index = 42 }
        };

        handler.EmbedMetadata(epubPath, modified);
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("Test Title", result.Title);
        Assert.HasCount(1, result.Authors);
        Assert.AreEqual("Test Author", result.Authors[0].Name);
        Assert.AreEqual("en", result.Language);
        Assert.AreEqual("2025-01-01", result.PublicationDate);
        Assert.AreEqual("A test description", result.Description);
        Assert.AreEqual("Test Publisher", result.Publisher);
        Assert.AreEqual("Test Series", result.Series?.Name);
        Assert.AreEqual(42, result.Series?.Index);
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesSeries_ExtractsCorrectly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        var modified = new Book
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            Series = new Series { Name = "New Series Name", Index = 7 }
        };

        handler.EmbedMetadata(epubPath, modified);
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("New Series Name", result.Series?.Name);
        Assert.AreEqual(7, result.Series?.Index);
    }

    [TestMethod]
    public void EmbedMetadata_ClearsSeries_RemovesEpub3ButCalibreFallbackRemains()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());
        Assert.IsNotNull(original.Series?.Name, "Test book should have a series.");

        var modified = new Book
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            Series = null
        };

        handler.EmbedMetadata(epubPath, modified);
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        // EPUB3 series tags are cleared, but calibre tags are preserved (we don't touch other tools' data).
        // The calibre fallback kicks in on re-read.
        Assert.AreEqual("The Last Kingdom", result.Series?.Name);
        Assert.AreEqual(1, result.Series?.Index);
    }

    [TestMethod]
    public void EmbedMetadata_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubFileHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.EmbedMetadata(bogusPath, new Book { Title = "x", Authors = [new Author { Name = "a" }], Language = "en" }));
    }

    [TestMethod]
    public void EmbedCover_ReplacesExistingCover_ExtractsCorrectly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var originalCoverDir = Path.Combine(outputDir, "original-cover");
        var (_, originalCoverFilePath) = handler.ExtractAll(epubPath, originalCoverDir, Guid.NewGuid());
        Assert.IsNotNull(originalCoverFilePath);
        var originalCoverSize = new FileInfo(originalCoverFilePath).Length;

        // Create a small but valid JPEG (minimal JFIF).
        var fakeCover = CreateMinimalJpeg();

        handler.EmbedCover(epubPath, fakeCover, "image/jpeg");

        var newCoverDir = Path.Combine(outputDir, "new-cover");
        var (_, newCoverFilePath) = handler.ExtractAll(epubPath, newCoverDir, Guid.NewGuid());
        Assert.IsNotNull(newCoverFilePath);

        var newCoverSize = new FileInfo(newCoverFilePath).Length;
        Assert.AreEqual(fakeCover.Length, newCoverSize, "Extracted cover should match the embedded cover size.");
        Assert.AreNotEqual(originalCoverSize, newCoverSize, "Cover size should differ from original.");
    }

    [TestMethod]
    public void EmbedCover_CreatesCoverXhtmlAsFirstSpineItem()
    {
        var handler = new EpubFileHandler();
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
        var handler = new EpubFileHandler();
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
        var handler = new EpubFileHandler();
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
        var handler = new EpubFileHandler();
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
        var handler = new EpubFileHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.EmbedCover(bogusPath, [0xFF, 0xD8], "image/jpeg"));
    }

    [TestMethod]
    public void RemoveCover_RemovesCoverFromEpub()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Verify cover exists before removal.
        var beforeDir = Path.Combine(outputDir, "before-cover");
        var (_, beforeCoverFilePath) = handler.ExtractAll(epubPath, beforeDir, Guid.NewGuid());
        Assert.IsNotNull(beforeCoverFilePath, "Test book should have a cover before removal.");

        handler.RemoveCover(epubPath);

        // Verify cover is gone after removal.
        var afterDir = Path.Combine(outputDir, "after-cover");
        var (_, afterCoverFilePath) = handler.ExtractAll(epubPath, afterDir, Guid.NewGuid());
        Assert.IsNull(afterCoverFilePath, "Cover should be null after removal.");
    }

    [TestMethod]
    public void RemoveCover_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubFileHandler();
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
    public void Extract_EpubWithAlexandriaIdentifier_IgnoresIt()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        InjectDcIdentifier(epubPath, $"alexandria:{Guid.NewGuid()}");

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual(Guid.Empty, result.BookUuid, "Alexandria UUID in dc:identifier should be ignored — identity comes from the database.");
    }

    [TestMethod]
    public void EmbedMetadata_RemovesExistingAlexandriaIdentifier()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        InjectDcIdentifier(epubPath, $"alexandria:{Guid.NewGuid()}");

        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());
        var modified = new Book
        {
            Title = "Changed Title",
            Authors = original.Authors,
            Language = original.Language,
            Series = original.Series
        };
        handler.EmbedMetadata(epubPath, modified);

        var opfXml = ReadOpfXml(epubPath);
        Assert.DoesNotContain("alexandria:", opfXml, "EmbedMetadata should remove existing Alexandria UUID identifiers.");
        Assert.AreEqual("Changed Title", handler.ExtractMetadata(epubPath).Title);
    }

    [TestMethod]
    public void Extract_ReturnsSubjects()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.IsNotEmpty(book.Subjects);
        Assert.Contains("Fantasy", book.Subjects);
        Assert.Contains("Fiction", book.Subjects);
        Assert.Contains("Historical", book.Subjects);
    }

    [TestMethod]
    public void Extract_ExcludesUnmappedContributorRoles()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.IsEmpty(book.Contributors);
    }

    [TestMethod]
    public void Extract_ReturnsIdentifiers()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.IsNotEmpty(book.Identifiers);
        Assert.IsTrue(book.Identifiers.Any(i => i.Contains("9788711291702")));
    }

    [TestMethod]
    public void Extract_ReturnsRightsAndSource()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.IsNotNull(book.Rights);
        Assert.StartsWith("All rights reserved", book.Rights);
        Assert.IsNotNull(book.Source);
        Assert.Contains("9788711291702", book.Source);
    }

    [TestMethod]
    public void EmbedMetadata_WritesEpub3SeriesOnly()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var modified = new Book
        {
            Title = "Test",
            Authors = [new Author { Name = "Author" }],
            Language = "en",
            Series = new Series { Name = "My Series", Index = 5 }
        };

        handler.EmbedMetadata(epubPath, modified);

        var opfXml = ReadOpfXml(epubPath);
        Assert.Contains("belongs-to-collection", opfXml);
        Assert.Contains("group-position", opfXml);
        Assert.Contains("collection-type", opfXml);
        Assert.DoesNotContain("alexandria:series", opfXml);
    }

    [TestMethod]
    public void EmbedMetadata_PreservesCalibreTags()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());
        var modified = new Book
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            Series = original.Series
        };

        handler.EmbedMetadata(epubPath, modified);

        var opfXml = ReadOpfXml(epubPath);
        Assert.Contains("calibre:series", opfXml);
        Assert.Contains("calibre:series_index", opfXml);
        Assert.Contains("calibre:timestamp", opfXml);
    }

    [TestMethod]
    public void Extract_Epub3SeriesWinsOverCalibre()
    {
        // The test EPUB has both calibre:series="The Last Kingdom" and
        // belongs-to-collection="The Last Kingdom". EPUB3 standard is read first.
        // Verify the EPUB3 value is used (same value here, but the priority path is EPUB3 first).
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Embed with a different series name — this writes EPUB3 only.
        var modified = new Book
        {
            Title = "Test",
            Authors = [new Author { Name = "Author" }],
            Language = "en",
            Series = new Series { Name = "EPUB3 Wins", Index = 99 }
        };
        handler.EmbedMetadata(epubPath, modified);

        // Now the EPUB has calibre:series="The Last Kingdom" + belongs-to-collection="EPUB3 Wins".
        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("EPUB3 Wins", result.Series?.Name);
        Assert.AreEqual(99, result.Series?.Index);
    }

    /// <summary>
    /// EPUB 3 permits multiple belongs-to-collection metas (each with its own id and refining
    /// group-position). Our domain and the Kobo sync protocol carry a single series per book,
    /// so when an EPUB declares more than one series-typed collection we take the first in
    /// document order and discard the rest — matching the stance we take for M4B grouping tags.
    /// </summary>
    [TestMethod]
    public void Extract_MultipleBelongsToCollection_TakesFirstSeries()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        InjectCollection(epubPath, collectionId: "series02", name: "Secondary Series", position: "7");

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("The Last Kingdom", result.Series?.Name);
        Assert.AreEqual(1, result.Series?.Index);
    }

    /// <summary>
    /// When an EPUB mixes a box-set-style collection (collection-type=collection) with a
    /// literary series (collection-type=series), we must prefer the series regardless of
    /// document order — a box set is a grouping, not a series, and shoving it into Book.Series
    /// would misrepresent the book.
    /// </summary>
    [TestMethod]
    public void Extract_CollectionTypeSeries_PreferredOverPlainCollection()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Wipe the existing EPUB3 series so we fully control the collection order below.
        RemoveEpub3SeriesFromEpub(epubPath);

        // Box set first (would win on document order alone), then the real series.
        InjectCollection(epubPath, collectionId: "boxset01", name: "Big Box Set", position: "1", collectionType: "collection");
        InjectCollection(epubPath, collectionId: "series01", name: "Real Series", position: "4", collectionType: "series");

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("Real Series", result.Series?.Name);
        Assert.AreEqual(4, result.Series?.Index);
    }

    /// <summary>
    /// Many EPUBs in the wild omit the collection-type refiner entirely. Treat an unlabeled
    /// belongs-to-collection as the fallback — take first-in-order. Required so we don't
    /// regress on EPUBs produced by tools that only emit belongs-to-collection + group-position.
    /// </summary>
    [TestMethod]
    public void Extract_UntypedBelongsToCollection_StillParsed()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        RemoveEpub3SeriesFromEpub(epubPath);
        InjectCollection(epubPath, collectionId: "c1", name: "Untyped Series", position: "2", collectionType: null);

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("Untyped Series", result.Series?.Name);
        Assert.AreEqual(2, result.Series?.Index);
    }

    [TestMethod]
    public void Extract_CalibreFallsBackWhenNoEpub3Series()
    {
        // The test EPUBs have both calibre and EPUB3 series. After removing EPUB3 tags,
        // the calibre fallback should kick in.
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        // Remove EPUB3 series tags, leaving only calibre.
        RemoveEpub3SeriesFromEpub(epubPath);

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());

        Assert.AreEqual("The Last Kingdom", result.Series?.Name);
        Assert.AreEqual(1, result.Series?.Index);
    }

    [TestMethod]
    public void EmbedMetadata_PreservesPassThroughDcElements()
    {
        var handler = new EpubFileHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var (original, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());
        Assert.IsNotEmpty(original.Subjects);
        Assert.IsNotNull(original.Rights);

        var modified = new Book
        {
            Title = "New Title",
            Authors = original.Authors,
            Language = original.Language,
            Series = original.Series
        };

        handler.EmbedMetadata(epubPath, modified);

        var (result, _) = handler.ExtractAll(epubPath, outputDir, Guid.NewGuid());
        Assert.IsNotEmpty(result.Subjects);
        Assert.Contains("Fantasy", result.Subjects);
        Assert.IsNotNull(result.Rights);
    }

    private Book ExtractBook(string fileName)
    {
        var extractor = new EpubFileHandler();
        var inputPath = Path.Combine(ContentBooksDir, fileName);
        return extractor.ExtractAll(inputPath, outputDir, Guid.NewGuid()).Book;
    }

    private static string ReadOpfXml(string epubPath)
    {
        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        using var opfStream = opfEntry.Open();
        using var reader = new StreamReader(opfStream);
        return reader.ReadToEnd();
    }

    private static void RemoveAlexandriaIdentifier(string epubPath)
    {
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";
        var dcNs = (System.Xml.Linq.XNamespace)"http://purl.org/dc/elements/1.1/";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update);

        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        System.Xml.Linq.XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = System.Xml.Linq.XDocument.Load(opfStream);

        var metadataEl = opfDoc.Descendants(opfNs + "metadata").First();
        foreach (var el in metadataEl.Elements(dcNs + "identifier").Where(e => e.Value.Trim().StartsWith("alexandria:", StringComparison.OrdinalIgnoreCase)).ToList())
            el.Remove();

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfEntry.FullName);
        using var writeStream = newEntry.Open();
        opfDoc.Save(writeStream);
    }

    private static void InjectDcIdentifier(string epubPath, string value)
    {
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";
        var dcNs = (System.Xml.Linq.XNamespace)"http://purl.org/dc/elements/1.1/";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update);

        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        System.Xml.Linq.XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = System.Xml.Linq.XDocument.Load(opfStream);

        var metadataEl = opfDoc.Descendants(opfNs + "metadata").First();

        // Remove any existing alexandria identifier.
        foreach (var el in metadataEl.Elements(dcNs + "identifier").Where(e => e.Value.Trim().StartsWith("alexandria:", StringComparison.OrdinalIgnoreCase)).ToList())
            el.Remove();

        metadataEl.Add(new System.Xml.Linq.XElement(dcNs + "identifier", value));

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfEntry.FullName);
        using var writeStream = newEntry.Open();
        opfDoc.Save(writeStream);
    }

    [TestMethod]
    public void ExtractAll_PopulatesWordCount()
    {
        var book = ExtractBook("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        Assert.IsNotNull(book.File.WordCount);
        Assert.IsGreaterThan(0, book.File.WordCount.Value);
    }

    [TestMethod]
    public void ExtractMetadata_PopulatesWordCount()
    {
        var handler = new EpubFileHandler();
        var inputPath = Path.Combine(ContentBooksDir, "Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var book = handler.ExtractMetadata(inputPath);

        Assert.IsNotNull(book.File.WordCount);
        Assert.IsGreaterThan(0, book.File.WordCount.Value);
    }

    private static void InjectCollection(string epubPath, string collectionId, string name, string position, string? collectionType = "series")
    {
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update);

        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        System.Xml.Linq.XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = System.Xml.Linq.XDocument.Load(opfStream);

        var metadataEl = opfDoc.Descendants(opfNs + "metadata").First();

        var collectionEl = new System.Xml.Linq.XElement(opfNs + "meta", name);
        collectionEl.SetAttributeValue("property", "belongs-to-collection");
        collectionEl.SetAttributeValue("id", collectionId);
        metadataEl.Add(collectionEl);

        if (collectionType is not null)
        {
            var typeEl = new System.Xml.Linq.XElement(opfNs + "meta", collectionType);
            typeEl.SetAttributeValue("property", "collection-type");
            typeEl.SetAttributeValue("refines", $"#{collectionId}");
            metadataEl.Add(typeEl);
        }

        var posEl = new System.Xml.Linq.XElement(opfNs + "meta", position);
        posEl.SetAttributeValue("property", "group-position");
        posEl.SetAttributeValue("refines", $"#{collectionId}");
        metadataEl.Add(posEl);

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfEntry.FullName);
        using var writeStream = newEntry.Open();
        opfDoc.Save(writeStream);
    }

    private static void RemoveEpub3SeriesFromEpub(string epubPath)
    {
        var opfNs = (System.Xml.Linq.XNamespace)"http://www.idpf.org/2007/opf";

        using var stream = new FileStream(epubPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update);

        var opfEntry = zip.Entries.First(e => e.FullName.EndsWith(".opf"));
        System.Xml.Linq.XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = System.Xml.Linq.XDocument.Load(opfStream);

        var metadataEl = opfDoc.Descendants(opfNs + "metadata").First();

        // Remove belongs-to-collection and its refines.
        var collectionMetas = metadataEl.Elements(opfNs + "meta").Concat(metadataEl.Elements("meta"))
            .Where(m => string.Equals(m.Attribute("property")?.Value, "belongs-to-collection", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var meta in collectionMetas)
        {
            var refineId = meta.Attribute("id")?.Value;
            if (refineId is not null)
            {
                metadataEl.Elements(opfNs + "meta").Concat(metadataEl.Elements("meta"))
                    .Where(m => m.Attribute("refines")?.Value == $"#{refineId}")
                    .ToList()
                    .ForEach(m => m.Remove());
            }
            meta.Remove();
        }

        opfEntry.Delete();
        var newEntry = zip.CreateEntry(opfEntry.FullName);
        using var writeStream = newEntry.Open();
        opfDoc.Save(writeStream);
    }
}
