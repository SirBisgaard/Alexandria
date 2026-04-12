using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
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
public sealed class AlexandriaLibraryServiceTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Books");

    private string testDir = null!;
    private string booksDir = null!;
    private string coversDir = null!;
    private string kepubsDir = null!;
    private string dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        booksDir = Path.Combine(testDir, "books");
        coversDir = Path.Combine(testDir, "covers");
        kepubsDir = Path.Combine(testDir, "kepubs");
        dbPath = Path.Combine(testDir, "alexandria.db");
        Directory.CreateDirectory(booksDir);
        Directory.CreateDirectory(coversDir);
        Directory.CreateDirectory(kepubsDir);
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));

        File.Copy(Path.Combine(ContentBooksDir, "Vasily Grossman - Life and Fate.epub"), Path.Combine(booksDir, "Vasily Grossman - Life and Fate.epub"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private AlexandriaSettings CreateSettings() => new() { PublicUrl = "http://localhost", DataFolder = testDir, EbookFolder = booksDir, AudiobookFolder = Path.Combine(testDir, "audiobooks") };

    private async Task<(AlexandriaLibraryService service, BookRepository bookRepository)> CreateService()
    {
        var settings = CreateSettings();
        var connectionProvider = new SqliteConnectionProvider(settings);
        var initializer = new DatabaseInitializer(connectionProvider, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
        var authorRepository = new AuthorRepository(connectionProvider);
        var seriesRepository = new SeriesRepository(connectionProvider);
        var bookRepository = new BookRepository(connectionProvider, authorRepository, seriesRepository);
        var coverThumbnailService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        var fileRepository = new FileRepository(settings, coverThumbnailService);
        var epubMetadataHandler = new EpubMetadataHandler();
        var kepubifyConverter = new KepubifyConverter();
        var epubIndexer = new FileEbookIndexer(settings, fileRepository, epubMetadataHandler, kepubifyConverter, coverThumbnailService);
        var epubMediaHandler = new FileEbbokMediaHandler(epubMetadataHandler, fileRepository);
        var m4BMetadataHandler = new M4BMetadataHandler(new FfmpegHandler());
        var audiobookMediaHandler = new FileAudiobookMediaHandler(m4BMetadataHandler);
        var audiobookIndexer = new FileAudiobookIndexer(settings, audiobookMediaHandler, fileRepository, coverThumbnailService);
        var contentManager = new FileContentManager(new IFileContentIndexer[] { epubIndexer, audiobookIndexer }, new IFileContentMediaHandler[] { epubMediaHandler, audiobookMediaHandler }, NullLogger<FileContentManager>.Instance);
        var indexerService = new AlexandriaIndexerService(contentManager, bookRepository, new AlexandriaSettingsService(new SettingsRepository(connectionProvider), new KoboTrafficLogRepository(connectionProvider), NullLogger<AlexandriaSettingsService>.Instance), authorRepository, seriesRepository, connectionProvider, NullLogger<AlexandriaIndexerService>.Instance);
        var readingStateRepository = new FakeReadingStateRepository();
        var collectionRepository = new FakeCollectionRepository();
        var service = new AlexandriaLibraryService(bookRepository, readingStateRepository, collectionRepository, contentManager, indexerService, authorRepository, seriesRepository);

        await indexerService.Index();
        return (service, bookRepository);
    }

    private static async Task<Book> FindLifeAndFate(BookRepository bookRepository)
    {
        var books = await bookRepository.GetAll();
        return books.Single(b => b.Title == "Life and Fate");
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_UpdatesTitleAndPreservesUuid()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);
        Assert.AreEqual("Life and Fate", original.Title);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = "Life and Fate (Updated)", Authors = original.Authors, Language = original.Language, Publisher = original.Publisher, Series = original.Series, PublicationDate = original.PublicationDate, Description = original.Description };

        var result = await service.UpdateBookMetadata(updateBook);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid, "UUID should be preserved after metadata edit.");
        Assert.AreEqual("Life and Fate (Updated)", result.Title);
        Assert.HasCount(original.Authors.Count, result.Authors);
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_UpdatesAllFields()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = "New Title", Authors = [new Author { Name = "Author A" }, new Author { Name = "Author B" }], Language = "da", Publisher = "Test Publisher", Series = new Series { Name = "Test Series", Index = 5 }, PublicationDate = "2025-01-01", Description = "A new description" };

        var result = await service.UpdateBookMetadata(updateBook);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid);
        Assert.AreEqual("New Title", result.Title);
        Assert.HasCount(2, result.Authors);
        Assert.AreEqual("Author A", result.Authors[0].Name);
        Assert.AreEqual("Author B", result.Authors[1].Name);
        Assert.AreEqual("da", result.Language);
        Assert.AreEqual("Test Publisher", result.Publisher);
        Assert.AreEqual("Test Series", result.Series?.Name);
        Assert.AreEqual(5, result.Series?.Index);
        Assert.AreEqual("2025-01-01", result.PublicationDate);
        Assert.AreEqual("A new description", result.Description);
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_MetadataPersistsInEpub()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = "Roundtrip Title", Authors = [new Author { Name = "Roundtrip Author" }], Language = "fr", Publisher = original.Publisher, Series = original.Series, PublicationDate = original.PublicationDate, Description = original.Description };

        await service.UpdateBookMetadata(updateBook);

        // Verify the EPUB file itself was modified by re-extracting metadata directly.
        var handler = new EpubMetadataHandler();
        var (freshMetadata, _) = handler.ExtractAll(original.SourcePath, coversDir, Guid.NewGuid());
        Assert.AreEqual("Roundtrip Title", freshMetadata.Title);
        Assert.HasCount(1, freshMetadata.Authors);
        Assert.AreEqual("Roundtrip Author", freshMetadata.Authors[0].Name);
        Assert.AreEqual("fr", freshMetadata.Language);
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_NoFileLocksAfterUpdate()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = "Lock Test", Authors = original.Authors, Language = original.Language };

        await service.UpdateBookMetadata(updateBook);

        // The EPUB, KEPUB, and cover files should all be writable after the operation completes.
        Assert.IsTrue(CanWriteFile(original.SourcePath), "EPUB file should not be locked after metadata update.");

        var updated = await bookRepository.FindByUuid(original.BookUuid);
        Assert.IsNotNull(updated);

        if (updated.KepubFilePath is not null)
        {
            Assert.IsTrue(CanWriteFile(updated.KepubFilePath), "KEPUB file should not be locked after metadata update.");
        }

        if (updated.CoverFilePath is not null)
        {
            Assert.IsTrue(CanWriteFile(updated.CoverFilePath), "Cover file should not be locked after metadata update.");
        }
    }

    private static bool CanWriteFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [TestMethod]
    public async Task RemoveBookCover_LifeAndFate_RemovesCoverAndUpdatesDb()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);
        Assert.IsNotNull(original.CoverFilePath, "Life and Fate should have a cover after indexing.");
        Assert.IsTrue(File.Exists(original.CoverFilePath));

        var result = await service.RemoveBookCover(original.BookUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid);
        Assert.IsNull(result.CoverFilePath, "CoverFilePath should be null after cover removal.");
        Assert.IsFalse(File.Exists(original.CoverFilePath), "Cover file should be deleted from disk.");
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_UnchangedMetadata_SkipsWrite()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var lastWriteTime = File.GetLastWriteTimeUtc(original.SourcePath);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = original.Title, Authors = original.Authors, Language = original.Language, Publisher = original.Publisher, Series = original.Series, PublicationDate = original.PublicationDate, Description = original.Description, Subtitle = original.Subtitle, Rights = original.Rights, IsAbridged = original.IsAbridged, Contributors = original.Contributors, Subjects = original.Subjects, Identifiers = original.Identifiers };

        var result = await service.UpdateBookMetadata(updateBook);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid);
        Assert.AreEqual(lastWriteTime, File.GetLastWriteTimeUtc(original.SourcePath), "EPUB file should not have been modified.");
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_ChangedTitle_WritesAndReindexes()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = "Changed Title", Authors = original.Authors, Language = original.Language, Publisher = original.Publisher, Series = original.Series, PublicationDate = original.PublicationDate, Description = original.Description };

        var result = await service.UpdateBookMetadata(updateBook);

        Assert.IsNotNull(result);
        Assert.AreEqual("Changed Title", result.Title);
    }

    [TestMethod]
    public async Task UpdateBookMetadata_NonExistentUuid_ReturnsNull()
    {
        var (service, _) = await CreateService();

        var result = await service.UpdateBookMetadata(new Book { BookUuid = Guid.NewGuid(), Title = "x", Authors = [new Author { Name = "a" }], Language = "en" });

        Assert.IsNull(result);
    }

    private class FakeReadingStateRepository : IReadingStateRepository
    {
        public Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid) => Task.FromResult<KoboStoreReadingState?>(null);
        public Task<ReadingProgress?> GetReadingProgress(Guid bookUuid) => Task.FromResult<ReadingProgress?>(null);
        public Task Upsert(Guid bookUuid, KoboStoreReadingState state) => Task.CompletedTask;
    }

    private class FakeCollectionRepository : ICollectionRepository
    {
        public Task<List<BookCollection>> GetAll() => Task.FromResult<List<BookCollection>>([]);
        public Task<BookCollection?> FindByUuid(Guid uuid) => Task.FromResult<BookCollection?>(null);
        public Task Upsert(BookCollection collection) => Task.CompletedTask;
        public Task Delete(Guid uuid) => Task.CompletedTask;
        public Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since) => Task.FromResult<List<BookCollection>>([]);
    }
}
