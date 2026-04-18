using System.Text.Json;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Externals.Models;
using Alexandria.DataAccess.Files.Services;
using Alexandria.Tests.TestData;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class M4BMetadataTests
{
    private string tempDir = null!;
    private string m4bPath = null!;
    private M4BFileHandler handler = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        GuidExtensions.SetNamespace(Guid.NewGuid());

        m4bPath = Fixtures.CopyM4BTo(tempDir);

        var settings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = tempDir,
            EbookFolder = Path.Combine(tempDir, "ebooks"),
            AudiobookFolder = Path.Combine(tempDir, "audiobooks")
        };
        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var coverService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        handler = new M4BFileHandler(ffmpeg, coverService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task M4BMetadata_ExtractMetadata_ReturnsKnownFixtureValues()
    {
        var book = await handler.ExtractMetadata(m4bPath);

        Assert.AreEqual("The Alexandria Test Audiobook", book.Title);
        Assert.HasCount(1, book.Authors);
        Assert.AreEqual("Alexandria Test", book.Authors[0].Name);
        Assert.IsNotNull(book.Series);
        Assert.AreEqual("Test Series", book.Series.Name);
        Assert.Contains("Audiobook", book.Subjects);
        Assert.IsNotNull(book.PublicationDate);
        Assert.StartsWith("2026", book.PublicationDate);
        Assert.IsNotNull(book.File.Duration);
        Assert.IsGreaterThan(6, book.File.Duration.Value);
        Assert.IsLessThan(10, book.File.Duration.Value);
        Assert.IsNotEmpty(book.Chapters);
    }

    [TestMethod]
    public async Task M4BMetadata_ExtractCoverBytes_ReturnsEmbeddedCover()
    {
        var coverBytes = await handler.ExtractCoverBytes(m4bPath);

        Assert.IsNotNull(coverBytes);
        Assert.IsTrue(IsJpegOrPng(coverBytes), "Embedded cover should be a valid JPEG or PNG.");
    }

    [TestMethod]
    public async Task M4BMetadata_ExtractCover_WritesCoverToDisk()
    {
        var coverOutputDir = Path.Combine(tempDir, "covers");
        Directory.CreateDirectory(coverOutputDir);

        var uuid = Guid.NewGuid();
        var coverFilePath = await handler.ExtractCover(m4bPath, coverOutputDir, uuid);

        Assert.IsNotNull(coverFilePath);
        Assert.IsTrue(File.Exists(coverFilePath));
        Assert.IsTrue(coverFilePath.StartsWith(coverOutputDir));
        Assert.IsGreaterThan(0, new FileInfo(coverFilePath).Length);
    }

    /// <summary>
    /// Writes every Book field the M4B handler supports and reads them back. Format/handler
    /// limitations intentionally not asserted: Language (not written by the handler), Subtitle
    /// (write-only — encoded into the album tag but never parsed back), Publisher (ffmpeg's MP4
    /// muxer drops the "publisher" tag — it's not in the standard MP4 tag set), Isbn/Identifiers
    /// (no standard MP4 tag), Source, Type, Relation, Coverage, Format. Multi-element Subjects
    /// are also limited: writer joins with "; " into the genre tag, reader returns the joined
    /// string as a single subject — so we assert single-subject round-trip only.
    /// </summary>
    [TestMethod]
    public async Task M4BMetadata_EmbedMetadata_RoundTripsAllSupportedFields()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;
        var originalChapterCount = book.Chapters.Count;

        book.Title = "Updated Audiobook";
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
        book.Series = new Series { Name = "New Series", Index = 3m };

        await handler.EmbedMetadata(book);

        var reread = await handler.ExtractMetadata(m4bPath);
        Assert.AreEqual("Updated Audiobook", reread.Title);
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
        Assert.AreEqual(3m, reread.Series.Index);
        Assert.HasCount(originalChapterCount, reread.Chapters);
    }

    [TestMethod]
    public async Task M4BMetadata_EmbedCover_ReplacesCover()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        var newCoverBytes = await File.ReadAllBytesAsync(coverPath);

        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;

        await handler.EmbedCover(book, newCoverBytes);

        var reread = await handler.ExtractCoverBytes(m4bPath);
        Assert.IsNotNull(reread);
        Assert.IsTrue(IsJpegOrPng(reread));
        Assert.IsGreaterThan(1024, reread.Length);
    }

    [TestMethod]
    public async Task M4BMetadata_Series_NameContainingHashSign_ParsesCorrectly()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;
        book.Series = new Series { Name = "C# Programming", Index = 2m };

        await handler.EmbedMetadata(book);

        var reread = await handler.ExtractMetadata(m4bPath);
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("C# Programming", reread.Series.Name);
        Assert.AreEqual(2m, reread.Series.Index);
    }

    [TestMethod]
    public async Task M4BMetadata_Grouping_MultiSeries_CollapsesToFirst()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        var chapters = book.Chapters
            .Select(c => new FfmpegChapter((long)Math.Round(c.StartTime * 1000), (long)Math.Round(c.EndTime * 1000), c.Title))
            .ToList();

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var tags = new Dictionary<string, string> { ["grouping"] = "First Series #1; Second Series #2" };
        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadata(m4bPath, tempPath, tags, chapters);
        File.Move(tempPath, m4bPath, overwrite: true);

        var reread = await handler.ExtractMetadata(m4bPath);
        Assert.IsNotNull(reread.Series);
        Assert.AreEqual("First Series", reread.Series.Name);
        Assert.AreEqual(1m, reread.Series.Index);
    }

    [TestMethod]
    public async Task M4BMetadata_Authors_ArtistTagSplitByComma()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        var chapters = BuildChapters(book);

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var tags = new Dictionary<string, string>
        {
            ["artist"] = "First Author, Second Author, Third Author",
            ["album_artist"] = "First Author, Second Author, Third Author"
        };
        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadata(m4bPath, tempPath, tags, chapters);
        File.Move(tempPath, m4bPath, overwrite: true);

        var reread = await handler.ExtractMetadata(m4bPath);
        Assert.HasCount(3, reread.Authors);
        Assert.AreEqual("First Author", reread.Authors[0].Name);
        Assert.AreEqual("Second Author", reread.Authors[1].Name);
        Assert.AreEqual("Third Author", reread.Authors[2].Name);
    }

    [TestMethod]
    public async Task M4BMetadata_Narrators_ReadFromComposerTag()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        var chapters = BuildChapters(book);

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var tags = new Dictionary<string, string> { ["composer"] = "Narrator One, Narrator Two" };
        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadata(m4bPath, tempPath, tags, chapters);
        File.Move(tempPath, m4bPath, overwrite: true);

        var reread = await handler.ExtractMetadata(m4bPath);
        Assert.HasCount(2, reread.Contributors);
        Assert.IsTrue(reread.Contributors.All(c => c.Role == "Narrator"));
        Assert.Contains("Narrator One", reread.Contributors.Select(c => c.Name));
        Assert.Contains("Narrator Two", reread.Contributors.Select(c => c.Name));
    }

    [TestMethod]
    public async Task M4BMetadata_EmbedCover_PngInput_NormalizedToJpeg()
    {
        byte[] pngBytes;
        using (var png = new MagickImage(MagickColors.CornflowerBlue, 128, 128))
        {
            png.Format = MagickFormat.Png;
            pngBytes = png.ToByteArray();
        }
        Assert.IsTrue(pngBytes[0] == 0x89 && pngBytes[1] == 0x50, "Test input should be PNG.");

        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;

        await handler.EmbedCover(book, pngBytes);

        var reread = await handler.ExtractCoverBytes(m4bPath);
        Assert.IsNotNull(reread);
        Assert.IsTrue(reread[0] == 0xFF && reread[1] == 0xD8, "Embedded cover should be re-encoded to JPEG.");
    }

    [TestMethod]
    public async Task M4BMetadata_EmbedMetadata_WritesOnlyStandardTags()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;
        book.Title = "Title After Embed";
        book.Series = new Series { Name = "Embedded Series", Index = 1m };

        await handler.EmbedMetadata(book);

        var ffmpeg = new FfmpegWrapper(NullLogger<FfmpegWrapper>.Instance);
        var json = await ffmpeg.GetMetadataJson(m4bPath);
        using var doc = JsonDocument.Parse(json);
        var tagsEl = doc.RootElement.GetProperty("format").GetProperty("tags");
        var keys = tagsEl.EnumerateObject().Select(p => p.Name).ToList();

        foreach (var key in keys)
        {
            Assert.IsFalse(key.StartsWith("----:", StringComparison.Ordinal), $"Tag '{key}' is a non-standard iTunes custom atom — must not be written.");
            Assert.IsFalse(key.StartsWith("TXXX", StringComparison.OrdinalIgnoreCase), $"Tag '{key}' is a custom ID3 frame — must not be written.");
        }
    }

    [TestMethod]
    public async Task M4BMetadata_StreamChapter_StreamsMp3Audio()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;
        Assert.IsNotEmpty(book.Chapters);

        using var output = new MemoryStream();
        await handler.StreamChapter(book, 0, output, CancellationToken.None);

        Assert.IsGreaterThan(0, output.Length);
        Assert.IsTrue(LooksLikeMp3(output.ToArray()), "Streamed bytes should look like an MP3 stream.");
    }

    [TestMethod]
    public async Task M4BMetadata_TranscodeChapterToFile_WritesMp3File()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;
        Assert.IsNotEmpty(book.Chapters);

        var outputPath = Path.Combine(tempDir, "chapter0.mp3");
        await handler.TranscodeChapterToFile(book, 0, outputPath, CancellationToken.None);

        Assert.IsTrue(File.Exists(outputPath));
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.IsGreaterThan(0, bytes.Length);
        Assert.IsTrue(LooksLikeMp3(bytes));
    }

    [TestMethod]
    public async Task M4BMetadata_RemoveCover_LeavesNoCover()
    {
        var book = await handler.ExtractMetadata(m4bPath);
        book.File.SourcePath = m4bPath;

        await handler.RemoveCover(book);

        var reread = await handler.ExtractCoverBytes(m4bPath);
        Assert.IsNull(reread);
    }

    private static bool IsJpegOrPng(byte[] data)
    {
        if (data.Length < 8)
            return false;
        if (data[0] == 0xFF && data[1] == 0xD8)
            return true;
        return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    }

    private static bool LooksLikeMp3(byte[] data)
    {
        if (data.Length < 3)
            return false;
        if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33)
            return true;
        return data[0] == 0xFF && (data[1] & 0xE0) == 0xE0;
    }

    private static List<FfmpegChapter> BuildChapters(Book book)
    {
        return book.Chapters
            .Select(c => new FfmpegChapter((long)Math.Round(c.StartTime * 1000), (long)Math.Round(c.EndTime * 1000), c.Title))
            .ToList();
    }
}
