using System.IO.Compression;
using System.Xml.Linq;
using Alexandria.DataAccess.Services;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class EpubMetadataExstractorTests
{
    private static readonly string ContentBooksDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Content", "Books"));

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
    public void Extract_ValidEpub_ReturnsTitleAndAuthor()
    {
        var extractor = new EpubMetadataExstractor();
        var inputPath = Directory.GetFiles(ContentBooksDir, "*.epub").First();

        var metadata = extractor.Extract(inputPath, outputDir);

        Assert.IsFalse(string.IsNullOrWhiteSpace(metadata.Title), "Title should not be empty.");
        Assert.IsGreaterThan(0, metadata.Authors.Count, "Should have at least one author.");
    }

    [TestMethod]
    public void Extract_ValidEpub_ExtractsCover()
    {
        var extractor = new EpubMetadataExstractor();
        var inputPath = Directory.GetFiles(ContentBooksDir, "*.epub").First();

        var metadata = extractor.Extract(inputPath, outputDir);

        Assert.IsTrue(metadata.CoverExtracted, "Cover should have been extracted.");
        Assert.IsNotNull(metadata.CoverFilePath);
        Assert.IsTrue(File.Exists(metadata.CoverFilePath), "Cover file should exist on disk.");
        Assert.IsGreaterThan(0, new FileInfo(metadata.CoverFilePath).Length, "Cover file should not be empty.");
    }

    [TestMethod]
    public void Extract_ValidEpub_ReturnsSeriesMetadata()
    {
        var extractor = new EpubMetadataExstractor();
        var inputPath = Directory.GetFiles(ContentBooksDir, "*.epub").First();

        var metadata = extractor.Extract(inputPath, outputDir);

        Assert.IsNotNull(metadata.SeriesName, "Series name should be set for these Calibre-produced EPUBs.");
        Assert.IsNotNull(metadata.SeriesIndex, "Series index should be set.");
    }

    [TestMethod]
    public void Extract_NonExistentFile_ThrowsFileNotFoundException()
    {
        var extractor = new EpubMetadataExstractor();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => extractor.Extract(bogusPath, outputDir));
    }

    [TestMethod]
    public void Extract_MultipleBooks_AllReturnMetadata()
    {
        var extractor = new EpubMetadataExstractor();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub").Take(3).ToArray();

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var metadata = extractor.Extract(epub, coverDir);

            Assert.IsFalse(string.IsNullOrWhiteSpace(metadata.Title),
                $"Title should not be empty for {Path.GetFileName(epub)}");
        }
    }
}
