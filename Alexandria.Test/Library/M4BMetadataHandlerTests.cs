using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Services;
using Microsoft.Extensions.Logging.Abstractions;

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
        Assert.HasCount(2, book.Authors);
        Assert.AreEqual("J.K. Rowling", book.Authors[0].Name);
        Assert.AreEqual("Newt Scamander", book.Authors[1].Name);
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
    public async Task Extract_ReturnsEmptyBookUuid()
    {
        var book = await ExtractBook(FantasticBeastsFileName);

        Assert.AreEqual(Guid.Empty, book.BookUuid, "Extracted BookUuid should be Empty — identity comes from the database, not the file.");
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
    public async Task EmbedMetadata_Roundtrip_StandardFields()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);

        book.Title = "Roundtrip Title";
        book.Authors = [new Author { Name = "Roundtrip Author" }];
        book.Contributors = [new Contributor { Name = "Roundtrip Narrator", Role = "Narrator" }];
        book.Description = "Roundtrip description text.";
        book.PublicationDate = "2025";
        book.Rights = "Copyright 2025 Roundtrip";
        book.Subjects = ["Roundtrip Genre"];
        book.Series = new Series { Name = "Roundtrip Series", Index = 5m };

        await handler.EmbedMetadata(m4BCopy, book);

        var re = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual("Roundtrip Title", re.Title);
        Assert.IsNotEmpty(re.Authors);
        Assert.AreEqual("Roundtrip Author", re.Authors[0].Name);
        Assert.IsNotEmpty(re.Contributors);
        Assert.AreEqual("Roundtrip Narrator", re.Contributors[0].Name);
        Assert.AreEqual("Roundtrip description text.", re.Description);
        Assert.AreEqual("2025", re.PublicationDate);
        Assert.AreEqual("Copyright 2025 Roundtrip", re.Rights);
        Assert.IsNotEmpty(re.Subjects);
        Assert.AreEqual("Roundtrip Genre", re.Subjects[0]);
        Assert.IsNotNull(re.Series);
        Assert.AreEqual("Roundtrip Series", re.Series.Name);
        Assert.AreEqual(5m, re.Series.Index);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_PreservesCover()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var coverDir = Path.Combine(outputDir, "cover-before");
        Directory.CreateDirectory(coverDir);
        var originalCoverPath = await handler.ExtractCover(m4BCopy, coverDir, Guid.NewGuid());
        Assert.IsNotNull(originalCoverPath, "Test file should have a cover.");

        var book = await handler.ExtractMetadata(m4BCopy);
        book.Title = "Cover Preservation Test";
        await handler.EmbedMetadata(m4BCopy, book);

        var reExtractDir = Path.Combine(outputDir, "cover-after");
        Directory.CreateDirectory(reExtractDir);
        var reExtractedCoverPath = await handler.ExtractCover(m4BCopy, reExtractDir, Guid.NewGuid());
        Assert.IsNotNull(reExtractedCoverPath, "Cover should still exist after metadata write.");
        var reExtractedBytes = await File.ReadAllBytesAsync(reExtractedCoverPath);
        Assert.AreEqual(0xFF, reExtractedBytes[0], "Cover should be JPEG.");
        Assert.AreEqual(0xD8, reExtractedBytes[1], "Cover should be JPEG.");
    }

    [TestMethod]
    public async Task EmbedCover_Roundtrip_ExtractMatchesEmbedded()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var coverDir = Path.Combine(outputDir, "source-cover");
        Directory.CreateDirectory(coverDir);
        var coverPath = await handler.ExtractCover(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), coverDir, Guid.NewGuid());
        Assert.IsNotNull(coverPath);
        var coverBytes = await File.ReadAllBytesAsync(coverPath);

        await handler.EmbedCover(m4BCopy, coverBytes, "image/jpeg");

        var extractDir = Path.Combine(outputDir, "re-extracted");
        Directory.CreateDirectory(extractDir);
        var reExtractedPath = await handler.ExtractCover(m4BCopy, extractDir, Guid.NewGuid());
        Assert.IsNotNull(reExtractedPath);
        var reExtractedBytes = await File.ReadAllBytesAsync(reExtractedPath);
        Assert.AreEqual(0xFF, reExtractedBytes[0], "Should be JPEG SOI marker.");
        Assert.AreEqual(0xD8, reExtractedBytes[1], "Should be JPEG SOI marker.");
    }

    [TestMethod]
    public async Task EmbedCover_Roundtrip_PreservesMetadataAndChapters()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var original = await handler.ExtractMetadata(m4BCopy);
        var coverDir = Path.Combine(outputDir, "source-cover");
        Directory.CreateDirectory(coverDir);
        var coverPath = await handler.ExtractCover(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), coverDir, Guid.NewGuid());
        Assert.IsNotNull(coverPath);
        var coverBytes = await File.ReadAllBytesAsync(coverPath);

        await handler.EmbedCover(m4BCopy, coverBytes, "image/jpeg");

        var re = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual(original.Title, re.Title);
        Assert.IsNotEmpty(re.Authors);
        Assert.AreEqual(original.Authors[0].Name, re.Authors[0].Name);
        Assert.HasCount(original.Chapters.Count, re.Chapters);
    }

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

    [TestMethod]
    public async Task RemoveCover_Roundtrip_PreservesMetadataAndChapters()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var original = await handler.ExtractMetadata(m4BCopy);

        await handler.RemoveCover(m4BCopy);

        var re = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual(original.Title, re.Title);
        Assert.IsNotEmpty(re.Authors);
        Assert.AreEqual(original.Authors[0].Name, re.Authors[0].Name);
        Assert.HasCount(original.Chapters.Count, re.Chapters);
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_PreservesChapters()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var original = await handler.ExtractMetadata(m4BCopy);
        var originalChapterCount = original.Chapters.Count;
        var firstChapter = original.Chapters[0];
        var lastChapter = original.Chapters[^1];

        original.Title = "Chapter Preservation Test";
        await handler.EmbedMetadata(m4BCopy, original);

        var re = await handler.ExtractMetadata(m4BCopy);
        Assert.HasCount(originalChapterCount, re.Chapters);
        Assert.AreEqual(firstChapter.Title, re.Chapters[0].Title);
        Assert.AreEqual(firstChapter.StartTime, re.Chapters[0].StartTime, 0.1);
        Assert.AreEqual(firstChapter.EndTime, re.Chapters[0].EndTime, 0.1);
        Assert.AreEqual(lastChapter.Title, re.Chapters[^1].Title);
        Assert.AreEqual(lastChapter.StartTime, re.Chapters[^1].StartTime, 0.1);
        Assert.AreEqual(lastChapter.EndTime, re.Chapters[^1].EndTime, 0.1);

        for (var i = 1; i < re.Chapters.Count; i++)
            Assert.AreEqual(re.Chapters[i - 1].EndTime, re.Chapters[i].StartTime, 0.1, $"Chapter {i} should be consecutive.");
    }

    [TestMethod]
    public async Task EmbedMetadata_Roundtrip_PreservesAudioStream()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var originalSize = new FileInfo(m4BCopy).Length;

        var book = await handler.ExtractMetadata(m4BCopy);
        book.Title = "Audio Stream Test";
        await handler.EmbedMetadata(m4BCopy, book);

        var newSize = new FileInfo(m4BCopy).Length;
        var ratio = (double)newSize / originalSize;
        Assert.IsTrue(ratio > 0.95 && ratio < 1.05, $"File size changed by {(ratio - 1) * 100:F1}% — likely re-encoded instead of stream copy.");
    }

    [TestMethod]
    public async Task EmbedMetadata_SpecialCharacters_Roundtrip()
    {
        var m4BCopy = CopyM4BToOutputDir(FantasticBeastsFileName);
        var book = await handler.ExtractMetadata(m4BCopy);
        book.Title = "Title with = equals; semicolon # hash";

        await handler.EmbedMetadata(m4BCopy, book);

        var re = await handler.ExtractMetadata(m4BCopy);
        Assert.AreEqual("Title with = equals; semicolon # hash", re.Title);
    }

}
