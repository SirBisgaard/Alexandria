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
    public void Extract_AllBooks_ExtractCover()
    {
        var extractor = new EpubMetadataHandler();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var coverPath = extractor.ExtractCover(epub, coverDir);

            Assert.IsNotNull(coverPath, $"Cover should have been extracted for {Path.GetFileName(epub)}.");
            Assert.IsTrue(File.Exists(coverPath), $"Cover file should exist for {Path.GetFileName(epub)}.");
            Assert.IsGreaterThan(0, new FileInfo(coverPath).Length, $"Cover file should not be empty for {Path.GetFileName(epub)}.");
        }
    }

    [TestMethod]
    public void Extract_NonExistentFile_ThrowsFileNotFoundException()
    {
        var extractor = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => extractor.ExtractMetadata(bogusPath));
    }

    [TestMethod]
    public void Extract_SameBook_ProducesDeterministicUuid()
    {
        var extractor = new EpubMetadataHandler();
        var inputPath = Path.Combine(ContentBooksDir, "Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var first = extractor.ExtractMetadata(inputPath);
        var second = extractor.ExtractMetadata(inputPath);

        Assert.AreEqual(first.Uuid, second.Uuid, "UUID should be deterministic for the same title and authors.");
        Assert.AreNotEqual(Guid.Empty, first.Uuid);
    }

    [TestMethod]
    public void Extract_DifferentBooks_ProduceDifferentUuids()
    {
        var extractor = new EpubMetadataHandler();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");
        var uuids = new HashSet<Guid>();

        foreach (var epub in epubs)
        {
            var metadata = extractor.ExtractMetadata(epub);
            Assert.IsTrue(uuids.Add(metadata.Uuid), $"UUID collision for {Path.GetFileName(epub)}.");
        }
    }

    [TestMethod]
    public void EmbedMetadata_UpdatesTitle_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");
        var original = handler.ExtractMetadata(epubPath);

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
        var result = handler.ExtractMetadata(epubPath);

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
        var original = handler.ExtractMetadata(epubPath);

        var modified = new EpubMetadata
        {
            Title = original.Title,
            Authors = ["Author One", "Author Two"],
            Language = original.Language,
            SeriesName = original.SeriesName,
            SeriesIndex = original.SeriesIndex
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractMetadata(epubPath);

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
        var result = handler.ExtractMetadata(epubPath);

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
        var original = handler.ExtractMetadata(epubPath);

        var modified = new EpubMetadata
        {
            Title = original.Title,
            Authors = original.Authors,
            Language = original.Language,
            SeriesName = "New Series Name",
            SeriesIndex = 7
        };

        handler.EmbedMetadata(epubPath, modified);
        var result = handler.ExtractMetadata(epubPath);

        Assert.AreEqual("New Series Name", result.SeriesName);
        Assert.AreEqual(7, result.SeriesIndex);
    }

    [TestMethod]
    public void EmbedMetadata_ClearsSeries_ExtractsCorrectly()
    {
        var handler = new EpubMetadataHandler();
        var epubPath = CopyTestEpub("Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var original = handler.ExtractMetadata(epubPath);
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
        var result = handler.ExtractMetadata(epubPath);

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
        var originalCoverPath = handler.ExtractCover(epubPath, originalCoverDir);
        Assert.IsNotNull(originalCoverPath);
        var originalCoverSize = new FileInfo(originalCoverPath).Length;

        // Create a small but valid JPEG (minimal JFIF).
        var fakeCover = CreateMinimalJpeg();

        handler.EmbedCover(epubPath, fakeCover, "image/jpeg");

        var newCoverDir = Path.Combine(outputDir, "new-cover");
        var newCoverPath = handler.ExtractCover(epubPath, newCoverDir);
        Assert.IsNotNull(newCoverPath);

        var newCoverSize = new FileInfo(newCoverPath).Length;
        Assert.AreEqual(fakeCover.Length, newCoverSize, "Extracted cover should match the embedded cover size.");
        Assert.AreNotEqual(originalCoverSize, newCoverSize, "Cover size should differ from original.");
    }

    [TestMethod]
    public void EmbedCover_NonExistentFile_ThrowsFileNotFoundException()
    {
        var handler = new EpubMetadataHandler();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => handler.EmbedCover(bogusPath, [0xFF, 0xD8], "image/jpeg"));
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

    private EpubMetadata ExtractBook(string fileName)
    {
        var extractor = new EpubMetadataHandler();
        var inputPath = Path.Combine(ContentBooksDir, fileName);
        return extractor.ExtractMetadata(inputPath);
    }
}
