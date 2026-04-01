using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Alexandria.DataAccess.Repositories;
using Alexandria.DataAccess.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class LibraryIndexerServiceTests
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
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private AlexandriaSettings CreateSettings(string? booksFolder = null) => new() { Database = dbPath, BooksFolder = booksFolder ?? ContentBooksDir, CoversFolder = coversDir, KepubsFolder = kepubsDir };

    private async Task<BookRepository> CreateRepository(AlexandriaSettings settings)
    {
        var initializer = new DatabaseInitializer(settings, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
        return new BookRepository(settings);
    }

    [TestMethod]
    public async Task Index_NewBooks_IndexesAll()
    {
        var settings = CreateSettings();
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        var epubCount = Directory.GetFiles(ContentBooksDir, "*.epub").Length;
        Assert.AreEqual(epubCount, books.Count, "All books should be indexed.");

        foreach (var book in books)
        {
            Assert.AreNotEqual(string.Empty, book.Title);
            Assert.IsTrue(book.Authors.Count > 0);
            Assert.IsTrue(book.FileHash.StartsWith("sha256:"));
            Assert.IsNotNull(book.KepubFilePath);
            Assert.IsTrue(File.Exists(book.KepubFilePath), $"KEPUB should exist for {book.Title}");
            Assert.IsTrue(book.KepubFileSize > 0, $"KepubFileSize should be positive for {book.Title}");
            Assert.AreNotEqual(default(DateTime), book.CreatedAt, $"CreatedAt should be set for {book.Title}");
            Assert.AreNotEqual(default(DateTime), book.LastModified, $"LastModified should be set for {book.Title}");
            Assert.AreEqual(book.CreatedAt, book.LastModified, $"CreatedAt and LastModified should match on first index for {book.Title}");
        }
    }

    [TestMethod]
    public async Task Index_ExistingBooks_SkipsUnchanged()
    {
        var settings = CreateSettings();
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();
        var firstRunBooks = await bookRepository.GetAll();
        var firstRunCount = firstRunBooks.Count;
        var timestamps = firstRunBooks.ToDictionary(b => b.Uuid, b => (b.CreatedAt, b.LastModified));

        await indexer.Index();

        var secondRunBooks = await bookRepository.GetAll();
        Assert.AreEqual(firstRunCount, secondRunBooks.Count, "Running twice should not duplicate books.");
        foreach (var book in secondRunBooks)
        {
            Assert.AreEqual(timestamps[book.Uuid].CreatedAt, book.CreatedAt, $"CreatedAt should not change for {book.Title}");
            Assert.AreEqual(timestamps[book.Uuid].LastModified, book.LastModified, $"LastModified should not change for unchanged {book.Title}");
        }
    }

    [TestMethod]
    public async Task Index_EmptyFolder_NoErrors()
    {
        var emptyBooksDir = Path.Combine(testDir, "empty-books");
        Directory.CreateDirectory(emptyBooksDir);

        var settings = CreateSettings(emptyBooksDir);
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.AreEqual(0, books.Count);
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
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        // Index the book.
        await indexer.Index();
        var books = await bookRepository.GetAll();
        Assert.AreEqual(1, books.Count);
        var kepubPath = books[0].KepubFilePath!;
        Assert.IsTrue(File.Exists(kepubPath));

        // Delete the epub so the entry becomes stale.
        File.Delete(tempEpub);

        // Re-index — stale entry and its kepub should be removed.
        await indexer.Index();
        books = await bookRepository.GetAll();
        Assert.AreEqual(0, books.Count);
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
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();
        var books = await bookRepository.GetAll();
        Assert.AreEqual(1, books.Count);

        var originalCreatedAt = books[0].CreatedAt;
        var originalLastModified = books[0].LastModified;
        var originalHash = books[0].FileHash;

        // Simulate a file change by tampering with the stored hash.
        books[0].FileHash = "sha256:fake";
        await bookRepository.Update(books[0]);

        await Task.Delay(50);

        // Re-index: hash won't match, UUID will match -> update path.
        await indexer.Index();

        books = await bookRepository.GetAll();
        Assert.AreEqual(1, books.Count);
        var book = books[0];
        Assert.AreEqual(originalCreatedAt, book.CreatedAt, "CreatedAt must be preserved.");
        Assert.IsTrue(book.LastModified > originalLastModified, "LastModified must be updated.");
        Assert.AreEqual(originalHash, book.FileHash, "FileHash should be the real hash again.");
        Assert.IsTrue(book.KepubFileSize > 0, "KepubFileSize must be set.");
    }

    [TestMethod]
    public async Task Index_NonExistentFolder_NoErrors()
    {
        var settings = CreateSettings(Path.Combine(testDir, "does-not-exist"));
        var bookRepository = await CreateRepository(settings);
        var extractor = new EpubMetadataExtractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, bookRepository, new FileRepository(settings), extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        var books = await bookRepository.GetAll();
        Assert.AreEqual(0, books.Count);
    }
}
