using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Database;
using Alexandria.DataAccess.Database.Repositories;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class AudiobookIndexerTests
{
    private static readonly string ContentAudiobooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Audiobooks");
    private static readonly string FantasticBeastsFileName = "J.K. Rowling - Wizarding World #0 - Fantastic Beasts and Where to Find Them.m4b";

    private string testDir = null!;
    private string audiobookDir = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        audiobookDir = Path.Combine(testDir, "audiobooks");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(audiobookDir);
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
        RequireAudiobooks();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, recursive: true);
    }

    private static void RequireAudiobooks()
    {
        if (!Directory.Exists(ContentAudiobooksDir) || Directory.GetFiles(ContentAudiobooksDir, "*.m4b").Length == 0)
            Assert.Inconclusive("Audiobook test data not found — skipping.");
    }

    private void CopyTestAudiobookToTestDir()
    {
        File.Copy(Path.Combine(ContentAudiobooksDir, FantasticBeastsFileName), Path.Combine(audiobookDir, FantasticBeastsFileName));
    }

    private AlexandriaSettings CreateSettings(string? audiobookFolder = null) => new() { PublicUrl = "http://localhost", DataFolder = testDir, EbookFolder = Path.Combine(testDir, "empty-ebooks"), AudiobookFolder = audiobookFolder ?? audiobookDir };

    private async Task<(BookRepository bookRepository, AuthorRepository authorRepository, SeriesRepository seriesRepository, SqliteConnectionProvider connectionProvider)> CreateRepositories(AlexandriaSettings settings)
    {
        var connectionProvider = new SqliteConnectionProvider(settings);
        var initializer = new DatabaseInitializer(connectionProvider, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
        var authorRepository = new AuthorRepository(connectionProvider);
        var seriesRepository = new SeriesRepository(connectionProvider);
        var bookRepository = new BookRepository(connectionProvider, authorRepository, seriesRepository);
        return (bookRepository, authorRepository, seriesRepository, connectionProvider);
    }

    private static (AlexandriaIndexerService indexer, FileContentManager contentManager) CreateIndexerAndContentManager(AlexandriaSettings settings, BookRepository bookRepository, AuthorRepository authorRepository, SeriesRepository seriesRepository, SqliteConnectionProvider connectionProvider)
    {
        var coverThumbnailService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        var fileRepository = new FileRepository(settings, coverThumbnailService);
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var audioIndexer = new FileAudiobookIndexer(settings, mediaHandler, coverThumbnailService);
        var contentManager = new FileContentManager(new IFileContentIndexer[] { audioIndexer }, new IFileContentMediaHandler[] { mediaHandler }, NullLogger<FileContentManager>.Instance);
        var indexer = new AlexandriaIndexerService(contentManager, bookRepository, new AlexandriaSettingsService(new SettingsRepository(connectionProvider), new KoboTrafficLogRepository(connectionProvider), NullLogger<AlexandriaSettingsService>.Instance), authorRepository, seriesRepository, connectionProvider, NullLogger<AlexandriaIndexerService>.Instance);
        return (indexer, contentManager);
    }

    [TestMethod]
    public async Task Index_NewAudiobook_IndexesWithMetadata()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.HasCount(1, books);
        var book = books[0];
        Assert.AreEqual("Fantastic Beasts and Where to Find Them", book.Title);
        Assert.AreEqual(MediaType.Audiobook, book.MediaType);
        Assert.IsNotEmpty(book.Authors);
        Assert.IsGreaterThan(0, book.Duration!.Value);
        Assert.IsGreaterThan(0, book.TotalFileSize);
        Assert.AreNotEqual(default(DateTimeOffset), book.CreatedAt);
        Assert.AreEqual(book.CreatedAt, book.ModifiedAt);
    }

    [TestMethod]
    public async Task Index_NewAudiobook_ExtractsCover()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var book = books[0];
        Assert.IsNotNull(book.CoverFilePath);
        Assert.IsTrue(File.Exists(book.CoverFilePath), "Cover file should exist after indexing.");
    }

    [TestMethod]
    public async Task Index_NewAudiobook_IndexesAllChapters()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var book = books[0];
        Assert.HasCount(87, book.Chapters);
        foreach (var chapter in book.Chapters)
        {
            Assert.AreNotEqual(string.Empty, chapter.Title, $"Chapter {chapter.ChapterIndex} should have a title.");
            Assert.IsGreaterThan(0.0, chapter.Duration, $"Chapter {chapter.ChapterIndex} should have positive duration.");
        }
    }

    [TestMethod]
    public async Task ReadChapter_AfterIndex_ReturnsMp3Bytes()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, contentManager) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var book = books[0];

        var chapterBytes = await contentManager.GetChapterFile(book, 0);

        Assert.IsNotNull(chapterBytes);
        Assert.IsGreaterThan(0, chapterBytes.Length);
    }

    [TestMethod]
    public async Task ReadChapter_InvalidIndex_ReturnsNull()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, contentManager) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var book = books[0];

        var result = await contentManager.GetChapterFile(book, 9999);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Index_ExistingAudiobook_SkipsUnchanged()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();
        var firstRunBooks = await bookRepository.GetAll();
        var timestamps = firstRunBooks.ToDictionary(b => b.BookUuid, b => (b.CreatedAt, b.ModifiedAt));

        await indexer.Index();

        var secondRunBooks = await bookRepository.GetAll();
        Assert.HasCount(1, secondRunBooks);
        foreach (var book in secondRunBooks)
        {
            Assert.AreEqual(timestamps[book.BookUuid].CreatedAt, book.CreatedAt);
            Assert.AreEqual(timestamps[book.BookUuid].ModifiedAt, book.ModifiedAt);
        }
    }

    [TestMethod]
    public async Task Index_RemovedAudiobook_CleansUpDerivedFiles()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();
        var books = await bookRepository.GetAll();
        Assert.HasCount(1, books);
        var coverPath = books[0].CoverFilePath;

        File.Delete(Path.Combine(audiobookDir, FantasticBeastsFileName));

        await indexer.Index();

        books = await bookRepository.GetAll();
        Assert.IsEmpty(books);
        if (coverPath is not null)
            Assert.IsFalse(File.Exists(coverPath), "Cover should be deleted after audiobook removal.");
    }

    [TestMethod]
    public async Task Index_EmptyAudiobookFolder_NoErrors()
    {
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.IsEmpty(books);
    }

    [TestMethod]
    public async Task Index_NewAudiobook_PersistsAuthorAndSeries()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var (indexer, _) = CreateIndexerAndContentManager(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var allAuthors = await authorRepository.GetAll();
        Assert.IsNotEmpty(allAuthors);
        var rowling = allAuthors.FirstOrDefault(a => a.Name == "J.K. Rowling");
        Assert.IsNotNull(rowling, "Author should exist in Authors table.");

        var books = await bookRepository.GetAll();
        var book = books[0];
        Assert.IsNotEmpty(book.Authors);
        Assert.AreNotEqual(Guid.Empty, book.Authors[0].AuthorUuid);
    }

    // --- MediaHandler: MetadataEquals ---

    [TestMethod]
    public async Task MetadataEquals_UnmodifiedFile_ReturnsTrue()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var filePath = Path.Combine(audiobookDir, FantasticBeastsFileName);
        var book = mediaHandler.ExtractMetadata(filePath);

        var result = mediaHandler.MetadataEquals(book, filePath);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task MetadataEquals_DifferentTitle_ReturnsFalse()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var filePath = Path.Combine(audiobookDir, FantasticBeastsFileName);
        var book = mediaHandler.ExtractMetadata(filePath);
        book.Title = "Completely Different Title";

        var result = mediaHandler.MetadataEquals(book, filePath);

        Assert.IsFalse(result);
    }

    // --- MediaHandler: EmbedMetadata ---

    [TestMethod]
    public async Task EmbedMetadata_UpdatesFileMetadata()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var filePath = Path.Combine(audiobookDir, FantasticBeastsFileName);
        var book = mediaHandler.ExtractMetadata(filePath);
        book.Title = "Embedded Title";
        book.Authors = [new Author { Name = "Embedded Author" }];

        mediaHandler.EmbedMetadata(filePath, book);

        var reExtracted = mediaHandler.ExtractMetadata(filePath);
        Assert.AreEqual("Embedded Title", reExtracted.Title);
        Assert.IsNotEmpty(reExtracted.Authors);
        Assert.AreEqual("Embedded Author", reExtracted.Authors[0].Name);
    }

    // --- MediaHandler: EmbedCover / RemoveCover ---

    [TestMethod]
    public async Task EmbedCover_UpdatesFileCover()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var filePath = Path.Combine(audiobookDir, FantasticBeastsFileName);
        var coverDir = Path.Combine(testDir, "covers-original");
        Directory.CreateDirectory(coverDir);
        var originalCoverPath = mediaHandler.ExtractCover(filePath, coverDir, Guid.NewGuid());
        Assert.IsNotNull(originalCoverPath);
        var coverBytes = await File.ReadAllBytesAsync(originalCoverPath);

        mediaHandler.EmbedCover(filePath, coverBytes, "image/jpeg");

        var extractDir = Path.Combine(testDir, "covers-reextracted");
        Directory.CreateDirectory(extractDir);
        var reExtractedPath = mediaHandler.ExtractCover(filePath, extractDir, Guid.NewGuid());
        Assert.IsNotNull(reExtractedPath);
        Assert.IsTrue(File.Exists(reExtractedPath));
    }

    [TestMethod]
    public async Task RemoveCover_RemovesFileCover()
    {
        CopyTestAudiobookToTestDir();
        var settings = CreateSettings();
        var m4BHandler = new M4BMetadataHandler(new FfmpegHandler());
        var mediaHandler = new FileAudiobookMediaHandler(m4BHandler);
        var filePath = Path.Combine(audiobookDir, FantasticBeastsFileName);

        mediaHandler.RemoveCover(filePath);

        var extractDir = Path.Combine(testDir, "covers-after-remove");
        Directory.CreateDirectory(extractDir);
        var coverPath = mediaHandler.ExtractCover(filePath, extractDir, Guid.NewGuid());
        Assert.IsNull(coverPath, "Cover should not exist after removal.");
    }
}
