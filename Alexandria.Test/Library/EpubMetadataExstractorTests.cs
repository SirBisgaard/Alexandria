using Alexandria.Business.Models;
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
    public void Extract_Sværdsang_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #4 - Sværdsang.epub");

        Assert.AreEqual("Sværdsang", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(4, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_DetBrændendeLand_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #5 - Det brændende land.epub");

        Assert.AreEqual("Det brændende land", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(5, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_EnKongesDød_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #6 - En konges død.epub");

        Assert.AreEqual("En konges død", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(6, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_DenHedenskeHerre_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #7 - Den hedenske herre.epub");

        Assert.AreEqual("Den hedenske herre", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(7, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_DenTommeTrone_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #8 - Den tomme trone.epub");

        Assert.AreEqual("Den tomme trone", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(8, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_StormensKrigere_ReturnsCorrectMetadata()
    {
        var metadata = ExtractBook("Bernard Cornwell - The Last Kingdom #9 - Stormens krigere.epub");

        Assert.AreEqual("Stormens krigere", metadata.Title);
        Assert.Contains("Bernard Cornwell", metadata.Authors);
        Assert.AreEqual("The Last Kingdom", metadata.SeriesName);
        Assert.AreEqual(9, metadata.SeriesIndex);
    }

    [TestMethod]
    public void Extract_AllBooks_ExtractCover()
    {
        var extractor = new EpubMetadataExstractor();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var metadata = extractor.Extract(epub, coverDir);

            Assert.IsTrue(metadata.CoverExtracted, $"Cover should have been extracted for {Path.GetFileName(epub)}.");
            Assert.IsNotNull(metadata.CoverFilePath);
            Assert.IsTrue(File.Exists(metadata.CoverFilePath), $"Cover file should exist for {Path.GetFileName(epub)}.");
            Assert.IsGreaterThan(0, new FileInfo(metadata.CoverFilePath).Length, $"Cover file should not be empty for {Path.GetFileName(epub)}.");
        }
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
    public void Extract_SameBook_ProducesDeterministicUuid()
    {
        var extractor = new EpubMetadataExstractor();
        var inputPath = Path.Combine(ContentBooksDir, "Bernard Cornwell - The Last Kingdom #1 - Det sidste kongerige.epub");

        var first = extractor.Extract(inputPath, Path.Combine(outputDir, "run1"));
        var second = extractor.Extract(inputPath, Path.Combine(outputDir, "run2"));

        Assert.AreEqual(first.Uuid, second.Uuid, "UUID should be deterministic for the same title and authors.");
        Assert.AreNotEqual(Guid.Empty, first.Uuid);
    }

    [TestMethod]
    public void Extract_DifferentBooks_ProduceDifferentUuids()
    {
        var extractor = new EpubMetadataExstractor();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub");
        var uuids = new HashSet<Guid>();

        foreach (var epub in epubs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub));
            var metadata = extractor.Extract(epub, coverDir);
            Assert.IsTrue(uuids.Add(metadata.Uuid), $"UUID collision for {Path.GetFileName(epub)}.");
        }
    }

    private EpubMetadata ExtractBook(string fileName)
    {
        var extractor = new EpubMetadataExstractor();
        var inputPath = Path.Combine(ContentBooksDir, fileName);
        return extractor.Extract(inputPath, outputDir);
    }
}
