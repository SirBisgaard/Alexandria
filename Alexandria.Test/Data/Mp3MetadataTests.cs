using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Externals.Models;
using Alexandria.DataAccess.Files.Services;
using Alexandria.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class Mp3MetadataTests
{
    private string tempDir = null!;
    private string audiobookFolder = null!;
    private string bookFolder = null!;
    private Mp3FileHandler handler = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        GuidExtensions.SetNamespace(Guid.NewGuid());

        audiobookFolder = Path.Combine(tempDir, "audiobooks");
        Directory.CreateDirectory(audiobookFolder);
        bookFolder = Fixtures.CopyMp3AudiobookTo(audiobookFolder, "Alexandria Test - Test Audiobook");

        var settings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = tempDir,
            EbookFolder = Path.Combine(tempDir, "ebooks"),
            AudiobookFolder = audiobookFolder
        };
        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var coverService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        handler = new Mp3FileHandler(ffmpeg, coverService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Mp3Metadata_ExtractMetadata_ReturnsKnownFixtureValues()
    {
        var book = await handler.ExtractMetadata(bookFolder);

        Assert.AreEqual("The Alexandria Test Audiobook", book.Title);
        Assert.HasCount(1, book.Authors);
        Assert.AreEqual("Alexandria Test", book.Authors[0].Name);
        Assert.IsNotNull(book.Series);
        Assert.AreEqual("Test Series", book.Series.Name);
        Assert.Contains("Audiobook", book.Subjects);
        Assert.HasCount(4, book.Chapters);
        Assert.AreEqual("Chapter 1", book.Chapters[0].Title);
        Assert.AreEqual("Chapter 2", book.Chapters[1].Title);
        Assert.AreEqual("Chapter 3", book.Chapters[2].Title);
        Assert.AreEqual("Chapter 4", book.Chapters[3].Title);
    }

    [TestMethod]
    public async Task Mp3Metadata_ExtractMetadata_ChaptersInTrackOrderNotFilenameOrder()
    {
        var scrambledFolder = Path.Combine(audiobookFolder, "Scrambled Audiobook");
        Directory.CreateDirectory(scrambledFolder);
        File.Copy(Path.Combine(bookFolder, "1.mp3"), Path.Combine(scrambledFolder, "d.mp3"));
        File.Copy(Path.Combine(bookFolder, "2.mp3"), Path.Combine(scrambledFolder, "c.mp3"));
        File.Copy(Path.Combine(bookFolder, "3.mp3"), Path.Combine(scrambledFolder, "b.mp3"));
        File.Copy(Path.Combine(bookFolder, "4.mp3"), Path.Combine(scrambledFolder, "a.mp3"));

        var book = await handler.ExtractMetadata(scrambledFolder);

        Assert.HasCount(4, book.Chapters);
        Assert.AreEqual("Chapter 1", book.Chapters[0].Title);
        Assert.AreEqual("Chapter 2", book.Chapters[1].Title);
        Assert.AreEqual("Chapter 3", book.Chapters[2].Title);
        Assert.AreEqual("Chapter 4", book.Chapters[3].Title);
    }

    [TestMethod]
    public async Task Mp3Metadata_Series_NameContainingHashSign_ParsesCorrectly()
    {
        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;
        book.Series = new Series { Name = "C# Programming", Index = 2m };

        await handler.EmbedMetadata(book);

        var reread = await handler.ExtractMetadata(bookFolder);
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("C# Programming", reread.Series.Name);
        Assert.AreEqual(2m, reread.Series.Index);
    }

    [TestMethod]
    public async Task Mp3Metadata_Grouping_MultiSeries_CollapsesToFirst()
    {
        var firstMp3 = Path.Combine(bookFolder, "1.mp3");

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var tags = new Dictionary<string, string> { ["grouping"] = "First Series #1; Second Series #2" };
        var tempPath = firstMp3 + ".tmp";
        await ffmpeg.WriteMetadata(firstMp3, tempPath, tags, []);
        File.Move(tempPath, firstMp3, overwrite: true);

        var reread = await handler.ExtractMetadata(bookFolder);
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("First Series", reread.Series.Name);
        Assert.AreEqual(1m, reread.Series.Index);
    }

    [TestMethod]
    public async Task Mp3Metadata_Chapters_PersistFileNameForStreaming()
    {
        var book = await handler.ExtractMetadata(bookFolder);

        Assert.HasCount(4, book.Chapters);
        Assert.AreEqual("1.mp3", book.Chapters[0].FileName);
        Assert.AreEqual("2.mp3", book.Chapters[1].FileName);
        Assert.AreEqual("3.mp3", book.Chapters[2].FileName);
        Assert.AreEqual("4.mp3", book.Chapters[3].FileName);
    }

    [TestMethod]
    public async Task Mp3Metadata_EmbedMetadata_CoverSupplied_BypassesShortCircuit()
    {
        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;

        var fixtureCoverPath = Fixtures.CopyCoverTo(tempDir);
        var coverBytes = await File.ReadAllBytesAsync(fixtureCoverPath);

        var coverInFolder = Path.Combine(bookFolder, "cover.jpg");
        Assert.IsFalse(File.Exists(coverInFolder));

        await handler.EmbedMetadata(book, coverBytes);

        Assert.IsTrue(File.Exists(coverInFolder), "cover.jpg must be written even when metadata is otherwise unchanged — cover-supplied path bypasses the MetadataEquals short-circuit.");
    }

    [TestMethod]
    public async Task Mp3Metadata_Extract_ReturnsMetadataAndCoverInOnePass()
    {
        var (book, coverBytes) = await handler.Extract(bookFolder);

        Assert.AreEqual("The Alexandria Test Audiobook", book.Title);
        Assert.HasCount(4, book.Chapters);
        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes));
    }

    [TestMethod]
    public async Task Mp3Metadata_ExtractCover_WritesCoverToDisk()
    {
        var coverOutputDir = Path.Combine(tempDir, "covers");
        Directory.CreateDirectory(coverOutputDir);

        var coverFilePath = await handler.ExtractCover(bookFolder, coverOutputDir, Guid.NewGuid());

        Assert.IsNotNull(coverFilePath);
        Assert.IsTrue(File.Exists(coverFilePath));
        Assert.IsTrue(coverFilePath.StartsWith(coverOutputDir));
        Assert.IsGreaterThan(0, new FileInfo(coverFilePath).Length);
    }

    [TestMethod]
    public async Task Mp3Metadata_StreamChapter_StreamsRawMp3FromSource()
    {
        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;

        using var output = new MemoryStream();
        await handler.StreamChapter(book, 0, output, CancellationToken.None);

        var expected = await File.ReadAllBytesAsync(Path.Combine(bookFolder, book.Chapters[0].FileName!));
        CollectionAssert.AreEqual(expected, output.ToArray());
    }

    [TestMethod]
    public async Task Mp3Metadata_ExtractCoverBytes_ReturnsEmbeddedCoverWhenNoCoverJpg()
    {
        var coverBytes = await handler.ExtractCoverBytes(bookFolder);

        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes));
    }

    [TestMethod]
    public async Task Mp3Metadata_ExtractCoverBytes_PrefersCoverJpgFile()
    {
        var coverJpgPath = Path.Combine(bookFolder, "cover.jpg");
        var fixtureCoverPath = Fixtures.CopyCoverTo(tempDir);
        File.Copy(fixtureCoverPath, coverJpgPath);

        var coverBytes = await handler.ExtractCoverBytes(bookFolder);
        var expected = await File.ReadAllBytesAsync(coverJpgPath);

        Assert.IsNotNull(coverBytes);
        CollectionAssert.AreEqual(expected, coverBytes);
    }

    /// <summary>
    /// Writes every Book field the MP3 folder handler supports and reads them back. Format/handler
    /// limitations intentionally not asserted: Language (not written by the handler), Subtitle
    /// (write-only — encoded into the album tag but never parsed back), Publisher (ffmpeg's MP3
    /// muxer drops the "publisher" tag — it's not mapped to a standard ID3v2 frame in this
    /// invocation), Isbn/Identifiers, Source, Type, Relation, Coverage, Format. Multi-element
    /// Subjects are limited to single-subject round-trip: the writer joins with "; " into the
    /// genre tag and the reader returns the joined string as a single subject.
    /// </summary>
    [TestMethod]
    public async Task Mp3Metadata_EmbedMetadata_RoundTripsAllSupportedFields()
    {
        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;

        book.Title = "Updated Audiobook Title";
        book.Description = "Fresh description.";
        book.PublicationDate = "2024-06-15";
        book.Rights = "© 2024 Alexandria";
        book.Subjects = ["Fiction"];
        book.Authors =
        [
            new Author { Name = "First Author" },
            new Author { Name = "Second Author" }
        ];
        book.Contributors =
        [
            new Contributor { Name = "First Narrator", Role = "Narrator" },
            new Contributor { Name = "Second Narrator", Role = "Narrator" }
        ];
        book.Series = new Series { Name = "New Series", Index = 2m };

        await handler.EmbedMetadata(book);

        var reread = await handler.ExtractMetadata(bookFolder);
        Assert.AreEqual("Updated Audiobook Title", reread.Title);
        Assert.AreEqual("Fresh description.", reread.Description);
        Assert.AreEqual("2024-06-15", reread.PublicationDate);
        Assert.AreEqual("© 2024 Alexandria", reread.Rights);
        Assert.HasCount(1, reread.Subjects);
        Assert.AreEqual("Fiction", reread.Subjects[0]);
        Assert.HasCount(2, reread.Authors);
        Assert.AreEqual("First Author", reread.Authors[0].Name);
        Assert.AreEqual("Second Author", reread.Authors[1].Name);
        Assert.HasCount(2, reread.Contributors);
        Assert.IsTrue(reread.Contributors.All(c => c.Role == "Narrator"));
        Assert.Contains("First Narrator", reread.Contributors.Select(c => c.Name));
        Assert.Contains("Second Narrator", reread.Contributors.Select(c => c.Name));
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("New Series", reread.Series.Name);
        Assert.AreEqual(2m, reread.Series.Index);
        Assert.HasCount(4, reread.Chapters);
    }

    [TestMethod]
    public async Task Mp3Metadata_EmbedCover_WritesCoverJpgAndEmbedsInEveryFile()
    {
        var fixtureCoverPath = Fixtures.CopyCoverTo(tempDir);
        var coverData = await File.ReadAllBytesAsync(fixtureCoverPath);

        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;

        await handler.EmbedCover(book, coverData);

        Assert.IsTrue(File.Exists(Path.Combine(bookFolder, "cover.jpg")));

        File.Delete(Path.Combine(bookFolder, "cover.jpg"));

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        foreach (var mp3 in Directory.GetFiles(bookFolder, "*.mp3"))
        {
            var embedded = await ffmpeg.ExtractCoverImage(mp3);
            Assert.IsNotNull(embedded, $"MP3 {Path.GetFileName(mp3)} should carry an embedded cover.");
            Assert.IsTrue(IsJpegOrPng(embedded));
        }
    }

    [TestMethod]
    public async Task Mp3Metadata_RemoveCover_ClearsEveryFileAndDeletesCoverJpg()
    {
        var fixtureCoverPath = Fixtures.CopyCoverTo(tempDir);
        var coverData = await File.ReadAllBytesAsync(fixtureCoverPath);

        var book = await handler.ExtractMetadata(bookFolder);
        book.File.SourcePath = bookFolder;
        await handler.EmbedCover(book, coverData);

        await handler.RemoveCover(book);

        Assert.IsFalse(File.Exists(Path.Combine(bookFolder, "cover.jpg")));

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        foreach (var mp3 in Directory.GetFiles(bookFolder, "*.mp3"))
        {
            var embedded = await ffmpeg.ExtractCoverImage(mp3);
            Assert.IsNull(embedded, $"MP3 {Path.GetFileName(mp3)} should have no embedded cover.");
        }
    }

    private static bool IsJpegOrPng(byte[] data)
    {
        if (data.Length < 8)
            return false;
        if (data[0] == 0xFF && data[1] == 0xD8)
            return true;
        return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    }
}
