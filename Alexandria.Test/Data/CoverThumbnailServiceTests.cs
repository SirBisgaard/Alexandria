using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Services;
using Alexandria.Tests.TestData;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class CoverThumbnailServiceTests
{
    private string tempDir = null!;
    private string sourceCoverPath = null!;
    private CoverThumbnailService service = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        sourceCoverPath = Fixtures.CopyCoverTo(tempDir);

        var settings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = tempDir,
            EbookFolder = Path.Combine(tempDir, "ebooks"),
            AudiobookFolder = Path.Combine(tempDir, "audiobooks")
        };
        service = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_UncachedCover_ReturnsValidJpegBytes()
    {
        var bytes = await service.GetOrCreateThumbnail(sourceCoverPath, Guid.NewGuid(), 355, 530, 80);

        Assert.IsNotNull(bytes);
        Assert.IsTrue(IsJpeg(bytes));
        using var image = new MagickImage(bytes);
        Assert.IsLessThanOrEqualTo(355u, image.Width);
        Assert.IsLessThanOrEqualTo(530u, image.Height);
        Assert.IsGreaterThan(0u, image.Width);
        Assert.IsGreaterThan(0u, image.Height);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_SecondCall_ReturnsSameBytes()
    {
        var uuid = Guid.NewGuid();

        var first = await service.GetOrCreateThumbnail(sourceCoverPath, uuid, 355, 530, 80);
        var second = await service.GetOrCreateThumbnail(sourceCoverPath, uuid, 355, 530, 80);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_MissingSource_ReturnsNull()
    {
        var missing = Path.Combine(tempDir, "no-such-cover.jpg");

        var bytes = await service.GetOrCreateThumbnail(missing, Guid.NewGuid(), 355, 530, 80);

        Assert.IsNull(bytes);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_NullSource_ReturnsNull()
    {
        var bytes = await service.GetOrCreateThumbnail(null, Guid.NewGuid(), 355, 530, 80);

        Assert.IsNull(bytes);
    }

    [TestMethod]
    public async Task GetOrCreateThumbnail_DifferentDimensions_ReturnDifferentSizedImages()
    {
        var uuid = Guid.NewGuid();

        var small = await service.GetOrCreateThumbnail(sourceCoverPath, uuid, 149, 223, 70);
        var medium = await service.GetOrCreateThumbnail(sourceCoverPath, uuid, 355, 530, 80);

        Assert.IsNotNull(small);
        Assert.IsNotNull(medium);
        using var smallImage = new MagickImage(small);
        using var mediumImage = new MagickImage(medium);
        Assert.IsLessThan(mediumImage.Width, smallImage.Width);
        Assert.IsLessThan(mediumImage.Height, smallImage.Height);
    }

    [TestMethod]
    public void ConvertToJpeg_JpegInput_ReturnsValidJpeg()
    {
        var input = File.ReadAllBytes(sourceCoverPath);

        var output = service.ConvertToJpeg(input);

        Assert.IsNotNull(output);
        Assert.IsTrue(IsJpeg(output));
        using var image = new MagickImage(output);
        Assert.IsGreaterThan(0u, image.Width);
    }

    [TestMethod]
    public void ConvertToJpeg_PngInput_ReturnsValidJpeg()
    {
        byte[] pngBytes;
        using (var png = new MagickImage(MagickColors.CornflowerBlue, 32, 32))
        {
            png.Format = MagickFormat.Png;
            pngBytes = png.ToByteArray();
        }
        Assert.IsTrue(IsPng(pngBytes));

        var output = service.ConvertToJpeg(pngBytes);

        Assert.IsTrue(IsJpeg(output));
        using var roundTripped = new MagickImage(output);
        Assert.AreEqual(MagickFormat.Jpeg, roundTripped.Format);
    }

    [TestMethod]
    public async Task DeleteThumbnails_RemovesOnlyTargetUuid()
    {
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();

        var keptBefore = await service.GetOrCreateThumbnail(sourceCoverPath, kept, 355, 530, 80);
        await service.GetOrCreateThumbnail(sourceCoverPath, removed, 355, 530, 80);
        await service.GetOrCreateThumbnail(sourceCoverPath, removed, 149, 223, 70);

        service.DeleteThumbnails(removed);

        var keptAfter = await service.GetOrCreateThumbnail(sourceCoverPath, kept, 355, 530, 80);
        var regenerated = await service.GetOrCreateThumbnail(sourceCoverPath, removed, 355, 530, 80);

        Assert.IsNotNull(keptBefore);
        Assert.IsNotNull(keptAfter);
        CollectionAssert.AreEqual(keptBefore, keptAfter);
        Assert.IsNotNull(regenerated);
        Assert.IsTrue(IsJpeg(regenerated));
    }

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static bool IsPng(byte[] data) => data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
}
