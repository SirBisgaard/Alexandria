using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class CoverThumbnailServiceTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "Old", "TestData", "Books");

    private string testDir = null!;
    private string coversDir = null!;
    private AlexandriaSettings settings = null!;
    private CoverThumbnailService service = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        coversDir = Path.Combine(testDir, "covers");
        Directory.CreateDirectory(coversDir);
        settings = new AlexandriaSettings { PublicUrl = "http://localhost", DataFolder = testDir, EbookFolder = ContentBooksDir, AudiobookFolder = Path.Combine(testDir, "audiobooks") };
        service = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private string ExtractTestCover()
    {
        var extractor = new EpubMetadataHandler();
        var epub = Directory.GetFiles(ContentBooksDir, "*.epub").First();
        var (_, coverFilePath) = extractor.ExtractAll(epub, coversDir, Guid.NewGuid());
        Assert.IsNotNull(coverFilePath, "Test EPUB should have a cover.");
        return coverFilePath;
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_GeneratesThumbnail()
    {
        var coverPath = ExtractTestCover();
        var uuid = Guid.NewGuid();

        var result = await service.GetOrCreateThumbnail(coverPath, uuid, 355, 530, 80);

        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result);

        var expectedPath = Path.Combine(coversDir, "355x530q80", $"{uuid}.jpg");
        Assert.IsTrue(File.Exists(expectedPath), "Thumbnail file should be written to disk.");

        var diskBytes = await File.ReadAllBytesAsync(expectedPath);
        CollectionAssert.AreEqual(diskBytes, result, "Returned bytes should match the file on disk.");
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_ServesFromCache()
    {
        var coverPath = ExtractTestCover();
        var uuid = Guid.NewGuid();

        var first = await service.GetOrCreateThumbnail(coverPath, uuid, 355, 530, 80);
        var second = await service.GetOrCreateThumbnail(coverPath, uuid, 355, 530, 80);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        CollectionAssert.AreEqual(first, second, "Second call should return identical cached bytes.");
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_ReturnsNullForMissingCover()
    {
        var result = await service.GetOrCreateThumbnail("/nonexistent/cover.jpg", Guid.NewGuid(), 355, 530, 80);

        Assert.IsNull(result);
        Assert.IsFalse(Directory.Exists(Path.Combine(coversDir, "355x530q80")), "No subdirectory should be created for missing covers.");
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_ReturnsNullForNullPath()
    {
        var result = await service.GetOrCreateThumbnail(null, Guid.NewGuid(), 355, 530, 80);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_CreatesDifferentSizes()
    {
        var coverPath = ExtractTestCover();
        var uuid = Guid.NewGuid();

        var small = await service.GetOrCreateThumbnail(coverPath, uuid, 149, 223, 70);
        var medium = await service.GetOrCreateThumbnail(coverPath, uuid, 355, 530, 80);

        Assert.IsNotNull(small);
        Assert.IsNotNull(medium);
        Assert.IsTrue(Directory.Exists(Path.Combine(coversDir, "149x223q70")));
        Assert.IsTrue(Directory.Exists(Path.Combine(coversDir, "355x530q80")));
        Assert.AreNotEqual(small.Length, medium.Length, "Different sizes should produce different file sizes.");
    }

    [TestMethod]
    public async Task DeleteThumbnails_RemovesAllSizes()
    {
        var coverPath = ExtractTestCover();
        var uuid = Guid.NewGuid();

        await service.GetOrCreateThumbnail(coverPath, uuid, 355, 530, 80);
        await service.GetOrCreateThumbnail(coverPath, uuid, 149, 223, 70);
        await service.GetOrCreateThumbnail(coverPath, uuid, 1264, 1680, 80);

        service.DeleteThumbnails(uuid);

        Assert.IsFalse(File.Exists(Path.Combine(coversDir, "355x530q80", $"{uuid}.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(coversDir, "149x223q70", $"{uuid}.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(coversDir, "1264x1680q80", $"{uuid}.jpg")));
    }
}
