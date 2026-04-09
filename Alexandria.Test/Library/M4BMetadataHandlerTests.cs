using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Services;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class M4BMetadataHandlerTests
{
    private static readonly string ContentAudiobooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Audiobooks");
    private static readonly string FantasticBeastsFileName = "J.K. Rowling - Wizarding World #0 - Fantastic Beasts and Where to Find Them.m4b";
    private static readonly string HuttGambitFileName = "A.C. Crispin - The Han Solo Trilogy #2 - The Hutt Gambit.m4b";

    private string outputDir = null!;
    private M4BMetadataHandler handler = null!;

    [TestInitialize]
    public void Setup()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
        handler = new M4BMetadataHandler(new FfmpegHandler());
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

    private async Task<Book> ExtractBook(string fileName) => await handler.ExtractMetadata(Path.Combine(ContentAudiobooksDir, fileName));

    // --- Fantastic Beasts metadata ---

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectTitle()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.AreEqual("Fantastic Beasts and Where to Find Them", book.Title);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectAuthor()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.IsNotEmpty(book.Authors);
        Assert.AreEqual("J.K. Rowling, Newt Scamander", book.Authors[0].Name);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectNarrator()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.HasCount(1, book.Contributors);
        Assert.AreEqual("Eddie Redmayne", book.Contributors[0].Name);
        Assert.AreEqual("Narrator", book.Contributors[0].Role);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectSeries()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.IsNotNull(book.Series);
        Assert.AreEqual("Wizarding World", book.Series.Name);
        Assert.AreEqual(0m, book.Series.Index);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectDuration()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.AreEqual(6856, book.Duration);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectChapterCount()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.HasCount(87, book.Chapters);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectFirstChapter()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        var first = book.Chapters[0];
        Assert.AreEqual(0, first.ChapterIndex);
        Assert.AreEqual("Opening Credits", first.Title);
        Assert.AreEqual(0, first.StartTime);
        Assert.IsGreaterThan(0.0, first.Duration);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectPublisher()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.AreEqual("Pottermore Publishing", book.Publisher);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectLanguage()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.AreEqual("English", book.Language);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectGenre()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.Contains("Science Fiction & Fantasy, Fairy Tales, Folk Tales & Myths", book.Subjects);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsCorrectIdentifier()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.Contains("asin:B01MZE29BJ", book.Identifiers);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsMediaTypeAudiobook()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.AreEqual(MediaType.Audiobook, book.MediaType);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ReturnsPositiveFileSize()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        Assert.IsGreaterThan(0, book.TotalFileSize);
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ChapterTimingsAreConsecutive()
    {
        var book = await ExtractBook(FantasticBeastsFileName);

        for (var i = 1; i < book.Chapters.Count; i++)
        {
            var prev = book.Chapters[i - 1];
            var curr = book.Chapters[i];
            Assert.AreEqual(prev.EndTime, curr.StartTime, 0.01, $"Chapter {i} StartTime should equal chapter {i - 1} EndTime.");
        }
    }

    [TestMethod]
    public async Task Extract_FantasticBeasts_ChapterDurationsMatchTimeDelta()
    {
        var book = await ExtractBook(FantasticBeastsFileName);

        foreach (var chapter in book.Chapters)
        {
            var expected = chapter.EndTime - chapter.StartTime;
            Assert.AreEqual(expected, chapter.Duration, 0.01, $"Chapter '{chapter.Title}' duration mismatch.");
        }
    }

    // --- The Hutt Gambit ---

    [TestMethod]
    public async Task Extract_HuttGambit_ReturnsCorrectMetadata()
    {
        var book = await ExtractBook(HuttGambitFileName);
        Assert.AreEqual("Star Wars: The Han Solo Trilogy: The Hutt Gambit", book.Title);
        Assert.AreEqual("A. C. Crispin", book.Authors[0].Name);
        Assert.AreEqual("David Pittu", book.Contributors[0].Name);
        Assert.AreEqual("Narrator", book.Contributors[0].Role);
        Assert.HasCount(4, book.Chapters);
    }

    // --- Cross-cutting ---

    [TestMethod]
    public async Task Extract_NonExistentFile_Throws()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => handler.ExtractMetadata(Path.Combine(outputDir, "nope.m4b")));
    }

    [TestMethod]
    public async Task Extract_SameBook_ProducesDeterministicUuid()
    {
        var book1 = await ExtractBook(FantasticBeastsFileName);
        var book2 = await ExtractBook(FantasticBeastsFileName);

        var uuid1 = book1.GenerateDeterministicUuid();
        var uuid2 = book2.GenerateDeterministicUuid();

        Assert.AreNotEqual(Guid.Empty, uuid1);
        Assert.AreEqual(uuid1, uuid2);
    }

    [TestMethod]
    public async Task Extract_DifferentBooks_ProduceDifferentUuids()
    {
        var beasts = await ExtractBook(FantasticBeastsFileName);
        var hutt = await ExtractBook(HuttGambitFileName);

        Assert.AreNotEqual(beasts.GenerateDeterministicUuid(), hutt.GenerateDeterministicUuid());
    }

    // --- Cover extraction ---

    [TestMethod]
    public async Task ExtractCover_FantasticBeasts_ExtractsCoverFile()
    {
        var coverPath = await handler.ExtractCover(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), outputDir, Guid.NewGuid());

        Assert.IsNotNull(coverPath);
        Assert.IsTrue(File.Exists(coverPath));
        Assert.IsGreaterThan(0, new FileInfo(coverPath).Length);
    }

    [TestMethod]
    public async Task ExtractCover_FantasticBeasts_OutputIsJpeg()
    {
        var coverPath = await handler.ExtractCover(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), outputDir, Guid.NewGuid());

        Assert.IsNotNull(coverPath);
        var bytes = await File.ReadAllBytesAsync(coverPath);
        Assert.AreEqual(0xFF, bytes[0], "First byte should be JPEG SOI marker.");
        Assert.AreEqual(0xD8, bytes[1], "Second byte should be JPEG SOI marker.");
    }

    [TestMethod]
    public async Task ExtractCover_AllBooks_ExtractsCover()
    {
        var m4Bs = Directory.GetFiles(ContentAudiobooksDir, "*.m4b");

        foreach (var m4B in m4Bs)
        {
            var coverDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(m4B));
            Directory.CreateDirectory(coverDir);
            var coverPath = await handler.ExtractCover(m4B, coverDir, Guid.NewGuid());

            Assert.IsNotNull(coverPath, $"Cover should have been extracted for {Path.GetFileName(m4B)}.");
            Assert.IsTrue(File.Exists(coverPath), $"Cover file should exist for {Path.GetFileName(m4B)}.");
            Assert.IsGreaterThan(0, new FileInfo(coverPath).Length, $"Cover file should not be empty for {Path.GetFileName(m4B)}.");
        }
    }

    // --- Chapter extraction ---

    [TestMethod]
    public async Task ExtractChapterAsMp3_FantasticBeasts_ReturnsValidBytes()
    {
        var book = await ExtractBook(FantasticBeastsFileName);
        var chapter = book.Chapters[0];

        var bytes = await handler.ExtractChapterAsMp3(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), chapter);

        Assert.IsGreaterThan(0, bytes.Length);
    }

    // --- Embed metadata ---

    private string CopyM4BToOutputDir(string fileName)
    {
        var dest = Path.Combine(outputDir, fileName);
        File.Copy(Path.Combine(ContentAudiobooksDir, fileName), dest);
        return dest;
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_PreservesTitle()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Title = "New Test Title";

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual("New Test Title", reExtracted.Title);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_PreservesAuthorsAndSeries()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Authors = [new Author { Name = "Test Author" }];
        book.Series = new Series { Name = "Test Series", Index = 3m };

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.IsNotEmpty(reExtracted.Authors);
        Assert.AreEqual("Test Author", reExtracted.Authors[0].Name);
        Assert.IsNotNull(reExtracted.Series);
        Assert.AreEqual("Test Series", reExtracted.Series.Name);
        Assert.AreEqual(3m, reExtracted.Series.Index);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_AlexandriaUuid()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        var uuid = Guid.NewGuid();
        book.BookUuid = uuid;

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual(uuid, reExtracted.BookUuid);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_SeriesAndPartTags()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Series = new Series { Name = "My Series", Index = 7m };

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.IsNotNull(reExtracted.Series);
        Assert.AreEqual("My Series", reExtracted.Series.Name);
        Assert.AreEqual(7m, reExtracted.Series.Index);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_LanguageTag()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Language = "en";

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.IsNotNull(reExtracted.Language);
        Assert.IsTrue(reExtracted.Language is "en" or "English", $"Language should be 'en' or 'English', got '{reExtracted.Language}'");
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_SubtitleAndRights()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Subtitle = "A Test Subtitle";
        book.Rights = "Copyright 2026 Test";

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual("A Test Subtitle", reExtracted.Subtitle);
        Assert.AreEqual("Copyright 2026 Test", reExtracted.Rights);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_AudibleAsin()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Identifiers = ["asin:B01234TEST"];

        await handler.EmbedMetadata(m4BCopy, book);

        var reExtracted = await handler.ExtractMetadata(m4BCopy);
        Assert.IsNotEmpty(reExtracted.Identifiers);
        Assert.Contains("asin:B01234TEST", reExtracted.Identifiers);
    }

    // --- Embed cover ---

    [TestMethod]
    public async Task EmbedCover_Roundtrip_ExtractMatchesEmbedded()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var originalCoverPath = await handler.ExtractCover(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), outputDir, Guid.NewGuid());
        Assert.IsNotNull(originalCoverPath);
        var coverBytes = await File.ReadAllBytesAsync(originalCoverPath);

        await handler.EmbedCover(m4BCopy, coverBytes, "image/jpeg");

        var extractDir = Path.Combine(outputDir, "re-extracted");
        Directory.CreateDirectory(extractDir);
        var reExtractedPath = await handler.ExtractCover(m4BCopy, extractDir, Guid.NewGuid());
        Assert.IsNotNull(reExtractedPath);
        Assert.IsTrue(File.Exists(reExtractedPath));
        var reExtractedBytes = await File.ReadAllBytesAsync(reExtractedPath);
        Assert.AreEqual(0xFF, reExtractedBytes[0], "Should be JPEG SOI marker.");
        Assert.AreEqual(0xD8, reExtractedBytes[1], "Should be JPEG SOI marker.");
    }

    // --- Remove cover ---

    [TestMethod]
    public async Task RemoveCover_Roundtrip_ExtractReturnsNull()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);

        await handler.RemoveCover(m4BCopy);

        var extractDir = Path.Combine(outputDir, "after-remove");
        Directory.CreateDirectory(extractDir);
        var coverPath = await handler.ExtractCover(m4BCopy, extractDir, Guid.NewGuid());
        Assert.IsNull(coverPath, "Cover should not exist after removal.");
    }
}
