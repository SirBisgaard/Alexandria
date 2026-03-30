using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Alexandria.DataAccess.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class LibraryIndexerServiceTests
{
    private static readonly string ContentBooksDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Content", "Books"));

    private string testDir = null!;
    private string coversDir = null!;
    private string kepubsDir = null!;
    private string indexFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        coversDir = Path.Combine(testDir, "covers");
        kepubsDir = Path.Combine(testDir, "kepubs");
        indexFilePath = Path.Combine(testDir, "index.json");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(coversDir);
        Directory.CreateDirectory(kepubsDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, recursive: true);
    }

    private AlexandriaSettings CreateSettings(string? booksFolder = null) => new() { IndexFile = indexFilePath, BooksFolder = booksFolder ?? ContentBooksDir, CoversFolder = coversDir, KepubsFolder = kepubsDir };

    [TestMethod]
    public async Task Index_NewBooks_IndexesAll()
    {
        var settings = CreateSettings();
        var indexFileService = new IndexFileService(settings);
        var extractor = new EpubMetadataExstractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, indexFileService, extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        var epubCount = Directory.GetFiles(ContentBooksDir, "*.epub").Length;
        Assert.AreEqual(epubCount, indexFileService.Books.Count, "All books should be indexed.");

        foreach (var book in indexFileService.Books)
        {
            Assert.AreNotEqual(string.Empty, book.Title);
            Assert.IsTrue(book.Authors.Count > 0);
            Assert.IsTrue(book.FileHash.StartsWith("sha256:"));
            Assert.IsNotNull(book.KepubFilePath);
            Assert.IsTrue(File.Exists(book.KepubFilePath), $"KEPUB should exist for {book.Title}");
        }
    }

    [TestMethod]
    public async Task Index_ExistingBooks_SkipsUnchanged()
    {
        var settings = CreateSettings();
        var indexFileService = new IndexFileService(settings);
        var extractor = new EpubMetadataExstractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, indexFileService, extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();
        var firstRunCount = indexFileService.Books.Count;

        await indexer.Index();

        Assert.AreEqual(firstRunCount, indexFileService.Books.Count, "Running twice should not duplicate books.");
    }

    [TestMethod]
    public async Task Index_EmptyFolder_NoErrors()
    {
        var emptyBooksDir = Path.Combine(testDir, "empty-books");
        Directory.CreateDirectory(emptyBooksDir);

        var settings = CreateSettings(emptyBooksDir);
        var indexFileService = new IndexFileService(settings);
        var extractor = new EpubMetadataExstractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, indexFileService, extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        Assert.AreEqual(0, indexFileService.Books.Count);
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
        var indexFileService = new IndexFileService(settings);
        var extractor = new EpubMetadataExstractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, indexFileService, extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        // Index the book.
        await indexer.Index();
        Assert.AreEqual(1, indexFileService.Books.Count);
        var kepubPath = indexFileService.Books[0].KepubFilePath!;
        Assert.IsTrue(File.Exists(kepubPath));

        // Delete the epub so the entry becomes stale.
        File.Delete(tempEpub);

        // Re-index — stale entry and its kepub should be removed.
        await indexer.Index();
        Assert.AreEqual(0, indexFileService.Books.Count);
        Assert.IsFalse(File.Exists(kepubPath), "Orphaned KEPUB file should be deleted.");
    }

    [TestMethod]
    public async Task Index_NonExistentFolder_NoErrors()
    {
        var settings = CreateSettings(Path.Combine(testDir, "does-not-exist"));
        var indexFileService = new IndexFileService(settings);
        var extractor = new EpubMetadataExstractor();
        var converter = new KepubifyConverter();
        var indexer = new LibraryIndexerService(settings, indexFileService, extractor, converter, NullLogger<LibraryIndexerService>.Instance);

        await indexer.Index();

        Assert.AreEqual(0, indexFileService.Books.Count);
    }
}
