using Alexandria.Business.Authentication;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Database;
using Alexandria.DataAccess.Database.Repositories;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class IndexerServiceTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Books");

    private string testDir = null!;
    private string coversDir = null!;
    private string kepubsDir = null!;
    private string dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        coversDir = Path.Combine(testDir, "covers");
        kepubsDir = Path.Combine(testDir, "kepubs");
        dbPath = Path.Combine(testDir, "alexandria.db");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(coversDir);
        Directory.CreateDirectory(kepubsDir);
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private AlexandriaSettings CreateSettings(string? booksFolder = null) => new() { PublicUrl = "http://localhost", DataFolder = testDir, EbookFolder = booksFolder ?? ContentBooksDir, AudiobookFolder = Path.Combine(testDir, "audiobooks") };

    private async Task<(BookRepository bookRepository, AuthorRepository authorRepository, SeriesRepository seriesRepository, SqliteConnectionProvider connectionProvider)> CreateRepositories(AlexandriaSettings settings)
    {
        var connectionProvider = new SqliteConnectionProvider(settings);
        var initializer = new DatabaseInitializer(connectionProvider, new PasswordHasher(), NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
        var authorRepository = new AuthorRepository(connectionProvider);
        var seriesRepository = new SeriesRepository(connectionProvider);
        var bookRepository = new BookRepository(connectionProvider, authorRepository, seriesRepository);
        return (bookRepository, authorRepository, seriesRepository, connectionProvider);
    }

    private IndexerService CreateIndexer(AlexandriaSettings settings, BookRepository bookRepository, AuthorRepository authorRepository, SeriesRepository seriesRepository, SqliteConnectionProvider connectionProvider)
    {
        var epubMetadataHandler = new EpubMetadataHandler();
        var converter = new KepubifyConverter();
        var coverThumbnailService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        var fileRepository = new FileRepository(settings, coverThumbnailService);
        var epubIndexer = new FileEbookIndexer(settings, fileRepository, epubMetadataHandler, converter, coverThumbnailService);
        var epubMediaHandler = new FileEbbokMediaHandler(epubMetadataHandler, fileRepository);
        var m4BMetadataHandler = new M4BMetadataHandler(new FfmpegHandler());
        var audiobookMediaHandler = new FileAudiobookMediaHandler(m4BMetadataHandler);
        var audiobookIndexer = new FileAudiobookIndexer(settings, audiobookMediaHandler, coverThumbnailService);
        var contentManager = new FileContentManager(new IFileContentIndexer[] { epubIndexer, audiobookIndexer }, new IFileContentMediaHandler[] { epubMediaHandler, audiobookMediaHandler }, NullLogger<FileContentManager>.Instance);
        return new IndexerService(contentManager, bookRepository, new SettingsService(new SettingsRepository(connectionProvider), new KoboTrafficLogRepository(connectionProvider), NullLogger<SettingsService>.Instance), authorRepository, seriesRepository, connectionProvider, NullLogger<IndexerService>.Instance);
    }

    [TestMethod]
    public async Task Index_NewBooks_IndexesAll()
    {
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var epubCount = Directory.GetFiles(ContentBooksDir, "*.epub").Length;
        Assert.HasCount(epubCount, books, "All books should be indexed.");

        foreach (var book in books)
        {
            Assert.AreNotEqual(string.Empty, book.Title);
            Assert.IsNotEmpty(book.Authors);
            Assert.IsNotNull(book.File.KepubFilePath);
            Assert.IsTrue(File.Exists(book.File.KepubFilePath), $"KEPUB should exist for {book.Title}");
            Assert.IsGreaterThan(0, book.File.TotalFileSize, $"TotalFileSize should be positive for {book.Title}");
            Assert.AreNotEqual(default(DateTimeOffset), book.CreatedAt, $"CreatedAt should be set for {book.Title}");
            Assert.AreNotEqual(default(DateTimeOffset), book.ModifiedAt, $"LastModified should be set for {book.Title}");
            Assert.AreEqual(book.CreatedAt, book.ModifiedAt, $"CreatedAt and LastModified should match on first index for {book.Title}");
        }
    }

    [TestMethod]
    public async Task Index_ExistingBooks_SkipsUnchanged()
    {
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();
        var firstRunBooks = await bookRepository.GetAll();
        var firstRunCount = firstRunBooks.Count;
        var timestamps = firstRunBooks.ToDictionary(b => b.BookUuid, b => (b.CreatedAt, b.ModifiedAt));

        await indexer.Index();

        var secondRunBooks = await bookRepository.GetAll();
        Assert.HasCount(firstRunCount, secondRunBooks, "Running twice should not duplicate books.");
        foreach (var book in secondRunBooks)
        {
            Assert.AreEqual(timestamps[book.BookUuid].CreatedAt, book.CreatedAt, $"CreatedAt should not change for {book.Title}");
            Assert.AreEqual(timestamps[book.BookUuid].ModifiedAt, book.ModifiedAt, $"LastModified should not change for unchanged {book.Title}");
        }
    }

    [TestMethod]
    public async Task Index_EmptyFolder_NoErrors()
    {
        var emptyBooksDir = Path.Combine(testDir, "empty-books");
        Directory.CreateDirectory(emptyBooksDir);

        var settings = CreateSettings(emptyBooksDir);
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.IsEmpty(books);
    }

    [TestMethod]
    public async Task Index_StaleEntry_RemovedFromIndex()
    {
        var booksDir = Path.Combine(testDir, "books");
        Directory.CreateDirectory(booksDir);

        // Copy one epub to the temp books folder.
        var sourceEpub = Directory.GetFiles(ContentBooksDir, "*.epub").First();
        var tempEpub = Path.Combine(booksDir, Path.GetFileName(sourceEpub));
        File.Copy(sourceEpub, tempEpub);

        var settings = CreateSettings(booksDir);
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        // Index the book.
        await indexer.Index();
        var books = await bookRepository.GetAll();
        Assert.HasCount(1, books);
        var kepubPath = books[0].File.KepubFilePath!;
        Assert.IsTrue(File.Exists(kepubPath));

        // Delete the epub so the entry becomes stale.
        File.Delete(tempEpub);

        // Re-index — stale entry and its kepub should be removed.
        await indexer.Index();
        books = await bookRepository.GetAll();
        Assert.IsEmpty(books);
        Assert.IsFalse(File.Exists(kepubPath), "Orphaned KEPUB file should be deleted.");
    }

    [TestMethod]
    public async Task Index_ChangedBook_PreservesCreatedAtUpdatesLastModified()
    {
        var booksDir = Path.Combine(testDir, "books");
        Directory.CreateDirectory(booksDir);

        var sourceEpub = Directory.GetFiles(ContentBooksDir, "*.epub").First();
        var tempEpub = Path.Combine(booksDir, Path.GetFileName(sourceEpub));
        File.Copy(sourceEpub, tempEpub);

        var settings = CreateSettings(booksDir);
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();
        var books = await bookRepository.GetAll();
        Assert.HasCount(1, books);

        var originalCreatedAt = books[0].CreatedAt;
        var originalModifiedAt = books[0].ModifiedAt;

        // Simulate a file change by modifying the epub's metadata.
        var handler = new EpubMetadataHandler();
        var metadata = handler.ExtractMetadata(books[0].File.SourcePath);
        metadata.Title = "Modified Title For Change Test";
        handler.EmbedMetadata(books[0].File.SourcePath, metadata);

        await Task.Delay(50);

        // Re-index: hash won't match, UUID will match -> update path.
        await indexer.Index();

        books = await bookRepository.GetAll();
        Assert.HasCount(1, books);
        var book = books[0];
        Assert.AreEqual(originalCreatedAt, book.CreatedAt, "CreatedAt must be preserved.");
        Assert.IsTrue(book.ModifiedAt > originalModifiedAt, "LastModified must be updated.");
        Assert.IsGreaterThan(0, book.File.TotalFileSize, "TotalFileSize must be set.");
    }

    [TestMethod]
    public async Task Index_NewBooks_PersistsAuthorsAndSeries()
    {
        var settings = CreateSettings();
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();

        // --- Series assertions ---
        var seriesBooks = books.Where(b => b.Series is not null).ToList();
        Assert.IsNotEmpty(seriesBooks, "At least one book should have a series.");

        var allSeries = await seriesRepository.GetAll();
        Assert.IsNotEmpty(allSeries, "Series table should have entries after indexing.");
        var lastKingdom = allSeries.FirstOrDefault(s => s.Name == "The Last Kingdom");
        Assert.IsNotNull(lastKingdom, "The Last Kingdom series should exist.");

        foreach (var book in seriesBooks)
        {
            Assert.AreNotEqual(Guid.Empty, book.Series!.SeriesUuid, $"SeriesUuid should be non-empty on '{book.Title}'.");
            Assert.AreEqual(lastKingdom.SeriesUuid, book.Series.SeriesUuid, $"SeriesUuid FK should match Series table for '{book.Title}'.");
        }

        // --- Author assertions ---
        var allAuthors = await authorRepository.GetAll();
        Assert.IsNotEmpty(allAuthors, "Authors table should have entries after indexing.");
        var cornwell = allAuthors.FirstOrDefault(a => a.Name == "Bernard Cornwell");
        Assert.IsNotNull(cornwell, "Bernard Cornwell should exist in Authors table.");

        foreach (var book in seriesBooks)
        {
            Assert.IsNotEmpty(book.Authors, $"Book '{book.Title}' should have authors.");
            Assert.IsTrue(book.Authors.Any(a => a.Name == "Bernard Cornwell"), $"'{book.Title}' should link to Bernard Cornwell.");
            Assert.AreNotEqual(Guid.Empty, book.Authors[0].AuthorUuid, $"AuthorUuid should be non-empty on '{book.Title}'.");
        }
    }

    [TestMethod]
    public async Task Index_NonExistentFolder_NoErrors()
    {
        var settings = CreateSettings(Path.Combine(testDir, "does-not-exist"));
        var (bookRepository, authorRepository, seriesRepository, connectionProvider) = await CreateRepositories(settings);
        var indexer = CreateIndexer(settings, bookRepository, authorRepository, seriesRepository, connectionProvider);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.IsEmpty(books);
    }
}
