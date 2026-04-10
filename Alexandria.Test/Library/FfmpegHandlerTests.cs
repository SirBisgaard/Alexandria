using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class FfmpegHandlerTests
{
    private static readonly string ContentAudiobooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Audiobooks");

    private string outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        RequireAudiobooks();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);
    }

    private static void RequireAudiobooks()
    {
        if (!Directory.Exists(ContentAudiobooksDir) || Directory.GetFiles(ContentAudiobooksDir, "*.m4b").Length == 0)
            Assert.Inconclusive("Audiobook test data not found — skipping.");
    }

    private static string GetFirstM4B() => Directory.GetFiles(ContentAudiobooksDir, "*.m4b").First();

    private string CopyM4BToOutputDir()
    {
        var source = GetFirstM4B();
        var dest = Path.Combine(outputDir, Path.GetFileName(source));
        File.Copy(source, dest);
        return dest;
    }

    [TestMethod]
    public async Task ExtractChapterAsMp3_ValidM4b_CreatesMp3File()
    {
        var handler = new FfmpegHandler();
        var m4BPath = GetFirstM4B();
        var mp3Path = Path.Combine(outputDir, "chapter.mp3");

        await handler.ExtractChapterAsMp3(m4BPath, 0, 10, mp3Path);

        Assert.IsTrue(File.Exists(mp3Path), "MP3 file should be created.");
        Assert.IsGreaterThan(0, new FileInfo(mp3Path).Length, "MP3 file should not be empty.");
    }

    [TestMethod]
    public async Task ExtractChapterAsMp3_MultipleChapters_AllExtractSuccessfully()
    {
        var handler = new FfmpegHandler();
        var m4BPath = GetFirstM4B();

        double[][] chapters = [[0, 10], [10, 20], [20, 30]];

        for (var i = 0; i < chapters.Length; i++)
        {
            var mp3Path = Path.Combine(outputDir, $"chapter-{i}.mp3");
            await handler.ExtractChapterAsMp3(m4BPath, chapters[i][0], chapters[i][1], mp3Path);

            Assert.IsTrue(File.Exists(mp3Path), $"MP3 should be created for chapter {i}.");
            Assert.IsGreaterThan(0, new FileInfo(mp3Path).Length, $"MP3 should not be empty for chapter {i}.");
        }
    }

    [TestMethod]
    public async Task WriteMetadata_ValidM4b_CreatesOutputFile()
    {
        var handler = new FfmpegHandler();
        var m4BCopy = CopyM4BToOutputDir();
        var outputPath = Path.Combine(outputDir, "output.m4b");

        var tags = new Dictionary<string, string> { ["title"] = "Write Test Title", ["artist"] = "Write Test Author" };
        var chapters = new List<FfmpegChapter> { new(0, 5000, "Chapter 1"), new(5000, 10000, "Chapter 2") };

        await handler.WriteMetadata(m4BCopy, outputPath, tags, chapters);

        Assert.IsTrue(File.Exists(outputPath), "Output file should be created.");
        Assert.IsGreaterThan(0, new FileInfo(outputPath).Length);
    }

    [TestMethod]
    public async Task WriteMetadataWithCover_ValidM4b_EmbedsCover()
    {
        var ffmpegHandler = new FfmpegHandler();
        var m4BCopy = CopyM4BToOutputDir();
        var outputPath = Path.Combine(outputDir, "output-cover.m4b");

        var metadataHandler = new M4BMetadataHandler(ffmpegHandler, NullLogger<M4BMetadataHandler>.Instance);
        var book = await metadataHandler.ExtractMetadata(m4BCopy);
        var chapters = book.Chapters.Select(c => new FfmpegChapter((long)(c.StartTime * 1000), (long)(c.EndTime * 1000), c.Title)).ToList();
        var tags = new Dictionary<string, string> { ["title"] = book.Title };

        var coverData = await ffmpegHandler.ExtractCoverImage(m4BCopy);
        Assert.IsNotNull(coverData, "Source should have a cover to use for the test.");

        await ffmpegHandler.WriteMetadataWithCover(m4BCopy, outputPath, tags, chapters, coverData);

        var reExtracted = await ffmpegHandler.ExtractCoverImage(outputPath);
        Assert.IsNotNull(reExtracted, "Cover should be extractable from output.");
        Assert.AreEqual(0xFF, reExtracted[0]);
        Assert.AreEqual(0xD8, reExtracted[1]);
    }

    [TestMethod]
    public async Task WriteMetadataRemoveCover_ValidM4b_RemovesCover()
    {
        var handler = new FfmpegHandler();
        var m4BCopy = CopyM4BToOutputDir();
        var outputPath = Path.Combine(outputDir, "output-nocover.m4b");

        var tags = new Dictionary<string, string> { ["title"] = "No Cover Test" };
        var chapters = new List<FfmpegChapter> { new(0, 5000, "Chapter 1") };

        await handler.WriteMetadataRemoveCover(m4BCopy, outputPath, tags, chapters);

        var coverData = await handler.ExtractCoverImage(outputPath);
        Assert.IsNull(coverData, "Cover should not exist in output.");
    }
}
