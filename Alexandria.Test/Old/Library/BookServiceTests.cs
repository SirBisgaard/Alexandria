using Alexandria.Business.Authentication;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Extensions;
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

namespace Alexandria.Tests.Old.Library;

[TestClass]
public sealed class BookServiceTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "Old", "TestData", "Books");

    private string testDir = null!;
    private string booksDir = null!;
    private string coversDir = null!;
    private string kepubsDir = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        booksDir = Path.Combine(testDir, "books");
        coversDir = Path.Combine(testDir, "covers");
        kepubsDir = Path.Combine(testDir, "kepubs");
        Path.Combine(testDir, "alexandria.db");
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

    private async Task<(BookService service, BookRepository bookRepository)> CreateService()
    {
        var settings = CreateSettings();
        var connectionProvider = new SqliteConnectionProvider(settings);
        var initializer = new DatabaseInitializer(connectionProvider, new PasswordHasher(), settings, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
        var authorRepository = new AuthorRepository(connectionProvider);
        var seriesRepository = new SeriesRepository(connectionProvider);
        var bookRepository = new BookRepository(connectionProvider, authorRepository, seriesRepository);
        var coverThumbnailService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        var fileRepository = new FileRepository(settings, coverThumbnailService);
        var epubMetadataHandler = new EpubFileHandler();
        var kepubifyConverter = new KepubifyWrapper();
        var epubIndexer = new FileEbookIndexer(settings, fileRepository, epubMetadataHandler, kepubifyConverter, coverThumbnailService);
        var epubMediaHandler = new FileEbbokMediaHandler(epubMetadataHandler, fileRepository);
        var m4BMetadataHandler = new M4BFileHandler(new FfmpegWrapper());
        var audiobookMediaHandler = new FileM4BAudiobookMediaHandler(m4BMetadataHandler, settings);
        var settingsService = new SettingsService(new SettingsRepository(connectionProvider), new KoboTrafficLogRepository(connectionProvider), NullLogger<SettingsService>.Instance);
        var audiobookIndexer = new FileM4BAudiobookIndexer(settings, audiobookMediaHandler, coverThumbnailService, settingsService, m4BMetadataHandler, NullLogger<FileM4BAudiobookIndexer>.Instance);
        var contentManager = new FileContentManager(new IFileContentIndexer[] { epubIndexer, audiobookIndexer }, new IFileContentMediaHandler[] { epubMediaHandler, audiobookMediaHandler }, NullLogger<FileContentManager>.Instance);
        var indexerService = new IndexerService(contentManager, bookRepository, settingsService, authorRepository, seriesRepository, connectionProvider, [], NullLogger<IndexerService>.Instance);
        var readingStateRepository = new FakeReadingStateRepository();
        var service = new BookService(bookRepository, readingStateRepository, contentManager, indexerService);

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
        var handler = new EpubFileHandler();
        var (freshMetadata, _) = handler.ExtractAll(original.File.SourcePath, coversDir, Guid.NewGuid());
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
        Assert.IsTrue(CanWriteFile(original.File.SourcePath), "EPUB file should not be locked after metadata update.");

        var updated = await bookRepository.FindByUuid(original.BookUuid);
        Assert.IsNotNull(updated);

        if (updated.File.KepubFilePath is not null)
        {
            Assert.IsTrue(CanWriteFile(updated.File.KepubFilePath), "KEPUB file should not be locked after metadata update.");
        }

        if (updated.File.CoverFilePath is not null)
        {
            Assert.IsTrue(CanWriteFile(updated.File.CoverFilePath), "Cover file should not be locked after metadata update.");
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
        Assert.IsNotNull(original.File.CoverFilePath, "Life and Fate should have a cover after indexing.");
        Assert.IsTrue(File.Exists(original.File.CoverFilePath));

        var result = await service.RemoveBookCover(original.BookUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid);
        Assert.IsNull(result.File.CoverFilePath, "CoverFilePath should be null after cover removal.");
        Assert.IsFalse(File.Exists(original.File.CoverFilePath), "Cover file should be deleted from disk.");
    }

    [TestMethod]
    public async Task UpdateBookMetadata_LifeAndFate_UnchangedMetadata_SkipsWrite()
    {
        var (service, bookRepository) = await CreateService();

        var original = await FindLifeAndFate(bookRepository);

        var lastWriteTime = File.GetLastWriteTimeUtc(original.File.SourcePath);

        var updateBook = new Book { BookUuid = original.BookUuid, Title = original.Title, Authors = original.Authors, Language = original.Language, Publisher = original.Publisher, Series = original.Series, PublicationDate = original.PublicationDate, Description = original.Description, Subtitle = original.Subtitle, Rights = original.Rights, File = new BookFile { IsAbridged = original.File.IsAbridged }, Contributors = original.Contributors, Subjects = original.Subjects, Identifiers = original.Identifiers };

        var result = await service.UpdateBookMetadata(updateBook);

        Assert.IsNotNull(result);
        Assert.AreEqual(original.BookUuid, result.BookUuid);
        Assert.AreEqual(lastWriteTime, File.GetLastWriteTimeUtc(original.File.SourcePath), "EPUB file should not have been modified.");
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
}
