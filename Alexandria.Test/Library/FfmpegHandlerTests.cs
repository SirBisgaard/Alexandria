using Alexandria.DataAccess.Externals;

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
}
