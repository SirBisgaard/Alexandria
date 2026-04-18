using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.BusinessLayer;

[TestClass]
public sealed class FileMp3AudiobookConverterTests
{
    private string tempDir = null!;
    private AlexandriaSettings alexandriaSettings = null!;
    private FakeSettingsService settingsService = null!;
    private FakeM4BFileHandler m4bHandler = null!;
    private FakeFfmpegWrapper ffmpegWrapper = null!;
    private FakeCoverThumbnailService coverService = null!;
    private FakeIndexingProgressService progressService = null!;
    private FileMp3AudiobookConverter converter = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        var audiobookFolder = Path.Combine(tempDir, "audiobooks");
        var dataFolder = Path.Combine(tempDir, "data");
        Directory.CreateDirectory(audiobookFolder);
        Directory.CreateDirectory(dataFolder);

        alexandriaSettings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = dataFolder,
            EbookFolder = Path.Combine(tempDir, "ebooks"),
            AudiobookFolder = audiobookFolder
        };

        settingsService = new FakeSettingsService { Settings = new AppSettings { M4BFileTreatment = M4BFileTreatment.ConvertAtIndex } };
        m4bHandler = new FakeM4BFileHandler();
        ffmpegWrapper = new FakeFfmpegWrapper();
        coverService = new FakeCoverThumbnailService();
        progressService = new FakeIndexingProgressService();

        converter = new FileMp3AudiobookConverter(
            alexandriaSettings,
            settingsService,
            m4bHandler,
            ffmpegWrapper,
            coverService,
            progressService,
            NullLogger<FileMp3AudiobookConverter>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    // ---------- Guard conditions ----------

    [TestMethod]
    public async Task Convert_TreatmentNotConvertAtIndex_NoOp()
    {
        settingsService.Settings.M4BFileTreatment = M4BFileTreatment.Nothing;
        var source = CreatePlaceholderM4B("book.m4b");
        m4bHandler.SetExtractedMetadata(MakeBook("Ignored"));

        await converter.Convert([], CancellationToken.None);

        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual(0, m4bHandler.ExtractMetadataCallCount);
        Assert.AreEqual(0, ffmpegWrapper.TranscodeCalls.Count);
    }

    [TestMethod]
    public async Task Convert_AudiobookFolderMissing_NoOp()
    {
        Directory.Delete(alexandriaSettings.AudiobookFolder, recursive: true);

        await converter.Convert([], CancellationToken.None);

        Assert.AreEqual(0, m4bHandler.ExtractMetadataCallCount);
    }

    [TestMethod]
    public async Task Convert_NoM4BFiles_NoOp()
    {
        await converter.Convert([], CancellationToken.None);

        Assert.AreEqual(0, m4bHandler.ExtractMetadataCallCount);
    }

    // ---------- Happy path ----------

    [TestMethod]
    public async Task Convert_SingleM4B_CreatesNamedFolderWithChapters()
    {
        var book = SeedPlaceholderBook("Alexandria - Some Book.m4b", "Some Book", author: "Alexandria", chapters: 3);

        await converter.Convert([book], CancellationToken.None);

        var targetDir = Path.Combine(alexandriaSettings.AudiobookFolder, "Alexandria - Some Book");
        Assert.IsTrue(Directory.Exists(targetDir));
        Assert.IsFalse(File.Exists(book.File.SourcePath), "source M4B should be deleted on success");
        Assert.AreEqual(3, ffmpegWrapper.TranscodeCalls.Count);
        Assert.IsTrue(ffmpegWrapper.TranscodeCalls.All(c => c.OutPath.StartsWith(targetDir)));
    }

    [TestMethod]
    public async Task Convert_WithSeriesAndIndex_FolderNameIncludesSeriesHashIndex()
    {
        var book = SeedPlaceholderBook("whatever.m4b", "The Second Story", author: "Jane Doe", chapters: 1);
        book.Series = new Series { SeriesUuid = Guid.NewGuid(), Name = "Night Tales", Index = 2 };

        await converter.Convert([book], CancellationToken.None);

        Assert.IsTrue(Directory.Exists(Path.Combine(alexandriaSettings.AudiobookFolder, "Jane Doe - Night Tales #2 - The Second Story")));
    }

    [TestMethod]
    public async Task Convert_WithoutSeries_FolderNameOmitsSeriesSegment()
    {
        var book = SeedPlaceholderBook("whatever.m4b", "Standalone", author: "Jane Doe", chapters: 1);

        await converter.Convert([book], CancellationToken.None);

        Assert.IsTrue(Directory.Exists(Path.Combine(alexandriaSettings.AudiobookFolder, "Jane Doe - Standalone")));
    }

    [TestMethod]
    public async Task Convert_WithCover_WritesCoverJpgOnceAndPassesPathToEveryChapterInvocation()
    {
        var book = SeedPlaceholderBook("b.m4b", "CoverBook", author: "A", chapters: 2);
        m4bHandler.SetExtractedCover([0xFF, 0xD8, 0xFF, 0x99, 0x00]); // JPEG magic — no ConvertToJpeg call

        await converter.Convert([book], CancellationToken.None);

        var coverPath = Path.Combine(alexandriaSettings.AudiobookFolder, "A - CoverBook", "cover.jpg");
        Assert.IsTrue(File.Exists(coverPath));
        Assert.HasCount(2, ffmpegWrapper.TranscodeCalls);
        Assert.IsTrue(ffmpegWrapper.TranscodeCalls.All(c => c.CoverPath == coverPath));
        Assert.AreEqual(0, coverService.ConvertToJpegCallCount);
    }

    [TestMethod]
    public async Task Convert_NonJpegCover_RunsThroughCoverThumbnailServiceConvertToJpeg()
    {
        var book = SeedPlaceholderBook("b.m4b", "PngCover", author: "A", chapters: 1);
        m4bHandler.SetExtractedCover([0x89, 0x50, 0x4E, 0x47]); // PNG magic

        await converter.Convert([book], CancellationToken.None);

        Assert.AreEqual(1, coverService.ConvertToJpegCallCount);
    }

    [TestMethod]
    public async Task Convert_NoCoverAvailable_CoverJpgNotCreatedAndPassedAsNull()
    {
        var book = SeedPlaceholderBook("b.m4b", "NoCover", author: "A", chapters: 1);
        m4bHandler.SetExtractedCover(null);

        await converter.Convert([book], CancellationToken.None);

        var coverPath = Path.Combine(alexandriaSettings.AudiobookFolder, "A - NoCover", "cover.jpg");
        Assert.IsFalse(File.Exists(coverPath));
        Assert.IsNull(ffmpegWrapper.TranscodeCalls.Single().CoverPath);
    }

    [TestMethod]
    public async Task Convert_MultipleM4Bs_ReportsProgressPerBook()
    {
        var first = SeedPlaceholderBook("a.m4b", "First", author: "X", chapters: 1);
        var second = SeedPlaceholderBook("b.m4b", "Second", author: "Y", chapters: 1);

        await converter.Convert([first, second], CancellationToken.None);

        Assert.HasCount(2, progressService.ConvertingReports);
        Assert.AreEqual(2, progressService.ConvertingReports[^1].Total);
    }

    // ---------- Identity preservation ----------

    [TestMethod]
    public async Task Convert_WithExistingBook_FolderNameUsesDbCanonicalMetadataNotExtracted()
    {
        var sourcePath = CreatePlaceholderM4B("file.m4b");
        var existing = MakeBook("DB Title", authorName: "DB Author", chapters: 0);
        existing.File.SourcePath = sourcePath;
        m4bHandler.SetExtractedMetadata(MakeBook("File Title", authorName: "File Author", chapters: 2));

        await converter.Convert([existing], CancellationToken.None);

        Assert.IsTrue(Directory.Exists(Path.Combine(alexandriaSettings.AudiobookFolder, "DB Author - DB Title")));
        Assert.IsFalse(Directory.Exists(Path.Combine(alexandriaSettings.AudiobookFolder, "File Author - File Title")));
    }

    // ---------- Skip/rollback paths ----------

    [TestMethod]
    public async Task Convert_TargetFolderExists_SkippedAndSourcePreserved()
    {
        var source = CreatePlaceholderM4B("x.m4b");
        Directory.CreateDirectory(Path.Combine(alexandriaSettings.AudiobookFolder, "Alice - Blocked"));
        m4bHandler.SetExtractedMetadata(MakeBook("Blocked", authorName: "Alice", chapters: 1));

        await converter.Convert([], CancellationToken.None);

        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual(0, ffmpegWrapper.TranscodeCalls.Count);
    }

    [TestMethod]
    public async Task Convert_TranscodeFails_TargetFolderRolledBackAndSourcePreserved()
    {
        var source = CreatePlaceholderM4B("x.m4b");
        m4bHandler.SetExtractedMetadata(MakeBook("Doomed", authorName: "Author", chapters: 2));
        ffmpegWrapper.ThrowOnCall = 1;

        await converter.Convert([], CancellationToken.None);

        Assert.IsTrue(File.Exists(source));
        Assert.IsFalse(Directory.Exists(Path.Combine(alexandriaSettings.AudiobookFolder, "Author - Doomed")));
    }

    // ---------- Chapter cache cleanup ----------

    [TestMethod]
    public async Task Convert_WithExistingBook_CleansUpCachedChaptersFolderForThatUuid()
    {
        var sourcePath = CreatePlaceholderM4B("cached.m4b");
        var existing = MakeBook("Cached", authorName: "A", chapters: 0);
        existing.File.SourcePath = sourcePath;

        var chaptersCache = Path.Combine(alexandriaSettings.ChaptersFolder, existing.BookUuid.ToString());
        Directory.CreateDirectory(chaptersCache);
        File.WriteAllBytes(Path.Combine(chaptersCache, "1.mp3"), [0, 0, 0]);

        m4bHandler.SetExtractedMetadata(MakeBook("Cached", authorName: "A", chapters: 1));

        await converter.Convert([existing], CancellationToken.None);

        Assert.IsFalse(Directory.Exists(chaptersCache));
    }

    [TestMethod]
    public async Task Convert_NoExistingBook_DoesNotTouchChaptersFolder()
    {
        CreatePlaceholderM4B("newly.m4b");
        m4bHandler.SetExtractedMetadata(MakeBook("Newly", authorName: "A", chapters: 1));

        await converter.Convert([], CancellationToken.None);

        // ChaptersFolder may not exist at all — we just assert we didn't create or delete anything spurious.
        Assert.IsFalse(Directory.Exists(alexandriaSettings.ChaptersFolder) && Directory.GetFileSystemEntries(alexandriaSettings.ChaptersFolder).Length > 0);
    }

    // ---------- Cancellation ----------

    [TestMethod]
    public async Task Convert_CancellationRequested_CountsAsErrorAndSkipsTranscode()
    {
        // The outer loop catches all exceptions (including OperationCanceledException) and
        // increments errorCount rather than propagating. Cancellation therefore surfaces to
        // the caller as errorCount == book count, and no ffmpeg work is done.
        var book = SeedPlaceholderBook("x.m4b", "WillCancel", author: "A", chapters: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var errorCount = await converter.Convert([book], cts.Token);

        Assert.AreEqual(1, errorCount);
        Assert.IsEmpty(ffmpegWrapper.TranscodeCalls);
    }

    // ---------- Sanitization ----------

    [TestMethod]
    public async Task Convert_TitleWithInvalidFileNameChars_FolderNameSanitized()
    {
        // Build a title containing every platform-invalid filename char so the test holds on Windows + Linux.
        var invalids = new string(Path.GetInvalidFileNameChars().Where(c => c != '\0' && c != '\n' && c != '\r').ToArray());
        var book = SeedPlaceholderBook("weird.m4b", $"Bad{invalids}Title", author: "A", chapters: 1);

        await converter.Convert([book], CancellationToken.None);

        var created = Directory.GetDirectories(alexandriaSettings.AudiobookFolder).Single();
        var leaf = Path.GetFileName(created);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            Assert.IsFalse(leaf.Contains(invalid), $"Folder leaf should not contain 0x{(int)invalid:X2}: '{leaf}'");
    }

    // ---------- Helpers ----------

    private string CreatePlaceholderM4B(string fileName)
    {
        var path = Path.Combine(alexandriaSettings.AudiobookFolder, fileName);
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);
        return path;
    }

    /// <summary>
    /// Creates a placeholder M4B file on disk and returns a matching <see cref="Book"/> whose
    /// <c>SourcePath</c> points at it — ready to be passed in <c>existingBooks</c>. Also
    /// enqueues the same book on the fake handler so <c>ExtractMetadata(sourcePath)</c> returns
    /// it unchanged; the converter treats the existing book as canonical and overlays chapters
    /// from the extraction, which is a no-op when they're the same instance.
    /// </summary>
    private Book SeedPlaceholderBook(string fileName, string title, string author, int chapters)
    {
        var source = CreatePlaceholderM4B(fileName);
        var book = MakeBook(title, authorName: author, chapters: chapters);
        book.File.SourcePath = source;
        m4bHandler.SetExtractedMetadata(book);
        return book;
    }

    private static Book MakeBook(string title, string authorName = "Any Author", int chapters = 1)
    {
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = authorName }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Audiobook,
                SourcePath = "",
                AudioCodec = "aac",
                AudioBitrate = 64000
            }
        };
        for (var i = 0; i < chapters; i++)
        {
            book.Chapters.Add(new AudiobookChapter
            {
                ChapterIndex = i,
                Title = $"Chapter {i + 1}",
                StartTime = i * 10.0,
                EndTime = (i + 1) * 10.0,
                Duration = 10.0
            });
        }
        return book;
    }

    // ---------- Fakes ----------

    internal sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();
        public Task<AppSettings> Get() => Task.FromResult(Settings);
        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp) => Task.CompletedTask;
        public Task UpdateSettings(AppSettings settings) { Settings = settings; return Task.CompletedTask; }
    }

    internal sealed class FakeM4BFileHandler : IM4BFileHandler
    {
        public Queue<Book> MetadataPerCall { get; } = new();
        public byte[]? ExtractedCover { get; private set; }
        public int ExtractMetadataCallCount { get; private set; }

        public void SetExtractedMetadata(Book book) => MetadataPerCall.Enqueue(book);
        public void SetExtractedCover(byte[]? cover) => ExtractedCover = cover;

        public Task<Book> ExtractMetadata(string filePath)
        {
            ExtractMetadataCallCount++;
            if (MetadataPerCall.Count == 0)
                throw new InvalidOperationException("FakeM4BFileHandler.MetadataPerCall queue is empty; call SetExtractedMetadata first.");
            return Task.FromResult(MetadataPerCall.Dequeue());
        }

        public Task<byte[]?> ExtractCoverBytes(string filePath) => Task.FromResult(ExtractedCover);
        public Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid) => Task.FromResult<string?>(null);
        public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TranscodeChapterToFile(Book book, int chapterIndex, string outputPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EmbedMetadata(Book book) => Task.CompletedTask;
        public Task EmbedCover(Book book, byte[] coverData) => Task.CompletedTask;
        public Task RemoveCover(Book book) => Task.CompletedTask;
    }

    internal sealed class FakeFfmpegWrapper : IFfmpegWrapper
    {
        public List<(string InputPath, double Start, double End, int? Quality, string OutPath, IReadOnlyDictionary<string, string>? Tags, string? CoverPath)> TranscodeCalls { get; } = [];
        public int? ThrowOnCall { get; set; }

        private int callCounter;

        public Task TranscodeChapterToFile(string inputPath, double startTime, double endTime, int? qaLevel, string outputPath, IReadOnlyDictionary<string, string>? tags, string? coverPath, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref callCounter);
            lock (TranscodeCalls)
                TranscodeCalls.Add((inputPath, startTime, endTime, qaLevel, outputPath, tags, coverPath));
            if (ThrowOnCall is int n && call == n)
                throw new InvalidOperationException("Simulated ffmpeg failure");
            File.WriteAllBytes(outputPath, [0x49, 0x44, 0x33]); // ID3 header so the file is non-empty
            return Task.CompletedTask;
        }

        public Task StreamChapter(string inputPath, double startTime, double endTime, int? qaLevel, Stream outputStream, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetMetadataJson(string inputPath) => Task.FromResult("{}");
        public Task<byte[]?> ExtractCoverImage(string inputPath) => Task.FromResult<byte[]?>(null);
        public Task WriteMetadata(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters) => Task.CompletedTask;
        public Task WriteMetadataWithCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters, byte[] coverData) => Task.CompletedTask;
        public Task WriteMetadataRemoveCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters) => Task.CompletedTask;
    }

    internal sealed class FakeCoverThumbnailService : ICoverThumbnailService
    {
        public int ConvertToJpegCallCount { get; private set; }

        public Task<byte[]?> GetOrCreateThumbnail(string? coverFilePath, Guid uuid, int width, int height, int quality) => Task.FromResult<byte[]?>(null);
        public void DeleteThumbnails(Guid uuid) { }

        public byte[] ConvertToJpeg(byte[] imageData)
        {
            ConvertToJpegCallCount++;
            return [0xFF, 0xD8, 0xFF, 0xE0];
        }
    }

    internal sealed class FakeIndexingProgressService : IIndexingProgressService
    {
        public List<(string Title, int Index, int Total)> ConvertingReports { get; } = [];

        public void Subscribe(IProgress<IndexingReport> sink) { }

        public void ReportConverting(string bookTitle, int index, int total)
        {
            lock (ConvertingReports)
                ConvertingReports.Add((bookTitle, index, total));
        }

        public void ReportIndexing(string bookTitle, int index, int total) { }
        public void ReportSubTask(string title, int progress, int total) { }
        public void ReportDone() { }
    }
}
