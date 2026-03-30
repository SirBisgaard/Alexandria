using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Alexandria.DataAccess.Services;

namespace Alexandria.Tests.Library;

[TestClass]
public class IndexFileServiceTests
{
    private string testDir = null!;
    private string indexFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), $"alexandria-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        indexFilePath = Path.Combine(testDir, "index.yaml");
    }

    private AlexandriaSettings CreateSettings() => new() { IndexFile = indexFilePath, BooksFolder = testDir, CoversFolder = testDir, KepubsFolder = testDir };

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
    }

    [TestMethod]
    public async Task LoadAsync_MissingFile_ReturnsEmptyList()
    {
        var service = new IndexFileService(CreateSettings());
        await service.LoadAsync();
        Assert.AreEqual(0, service.Books.Count);
    }

    [TestMethod]
    public async Task LoadAsync_EmptyFile_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(indexFilePath, "");
        var service = new IndexFileService(CreateSettings());
        await service.LoadAsync();
        Assert.AreEqual(0, service.Books.Count);
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTrip_PreservesBooks()
    {
        var service = new IndexFileService(CreateSettings());
        service.Books =
        [
            new Book
            {
                Uuid = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                FileHash = "sha256:abc123",
                SourcePath = "/books/test.epub",
                Title = "Test Book",
                Authors = ["Author One", "Author Two"],
                Language = "en",
                PublicationDate = "2020-01-01",
                Description = "A test book",
                Publisher = "Test Publisher",
                SeriesName = "Test Series",
                SeriesIndex = 1,
                CoverFilePath = "/covers/a1b2c3d4.jpg",
                KepubFilePath = "/kepubs/a1b2c3d4.kepub.epub"
            }
        ];

        await service.SaveAsync();

        var loaded = new IndexFileService(CreateSettings());
        await loaded.LoadAsync();

        Assert.AreEqual(1, loaded.Books.Count);
        var book = loaded.Books[0];
        Assert.AreEqual(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), book.Uuid);
        Assert.AreEqual("sha256:abc123", book.FileHash);
        Assert.AreEqual("/books/test.epub", book.SourcePath);
        Assert.AreEqual("Test Book", book.Title);
        Assert.AreEqual(2, book.Authors.Count);
        Assert.AreEqual("Author One", book.Authors[0]);
        Assert.AreEqual("en", book.Language);
        Assert.AreEqual("2020-01-01", book.PublicationDate);
        Assert.AreEqual("A test book", book.Description);
        Assert.AreEqual("Test Publisher", book.Publisher);
        Assert.AreEqual("Test Series", book.SeriesName);
        Assert.AreEqual(1, book.SeriesIndex);
        Assert.AreEqual("/covers/a1b2c3d4.jpg", book.CoverFilePath);
        Assert.AreEqual("/kepubs/a1b2c3d4.kepub.epub", book.KepubFilePath);
    }

    [TestMethod]
    public async Task SaveAndLoad_NullableFieldsOmitted()
    {
        var service = new IndexFileService(CreateSettings());
        service.Books = [new Book { Uuid = Guid.NewGuid(), FileHash = "sha256:abc", SourcePath = "/test.epub", Title = "Minimal" }];

        await service.SaveAsync();
        var json = await File.ReadAllTextAsync(indexFilePath);

        Assert.IsFalse(json.Contains("description"));
        Assert.IsFalse(json.Contains("publisher"));
        Assert.IsFalse(json.Contains("seriesName"));
        Assert.IsFalse(json.Contains("coverFilePath"));
        Assert.IsFalse(json.Contains("kepubFilePath"));
    }

    [TestMethod]
    public async Task ComputeFileHashAsync_DeterministicResult()
    {
        var testFile = Path.Combine(testDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "hello world");

        var service = new IndexFileService(CreateSettings());
        var hash1 = await service.ComputeFileHashAsync(testFile);
        var hash2 = await service.ComputeFileHashAsync(testFile);

        Assert.AreEqual(hash1, hash2);
        Assert.IsTrue(hash1.StartsWith("sha256:"));
    }

    [TestMethod]
    public async Task ComputeFileHashAsync_DifferentContentDifferentHash()
    {
        var file1 = Path.Combine(testDir, "file1.txt");
        var file2 = Path.Combine(testDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "content one");
        await File.WriteAllTextAsync(file2, "content two");

        var service = new IndexFileService(CreateSettings());
        var hash1 = await service.ComputeFileHashAsync(file1);
        var hash2 = await service.ComputeFileHashAsync(file2);

        Assert.AreNotEqual(hash1, hash2);
    }

    [TestMethod]
    public void FindByHash_ReturnsMatchingBook()
    {
        var service = new IndexFileService(CreateSettings());
        var target = new Book { FileHash = "sha256:target", Title = "Target" };
        service.Books = [new Book { FileHash = "sha256:other", Title = "Other" }, target];

        var found = service.FindByHash("sha256:target");
        Assert.IsNotNull(found);
        Assert.AreEqual("Target", found.Title);
    }

    [TestMethod]
    public void FindByHash_NoMatch_ReturnsNull()
    {
        var service = new IndexFileService(CreateSettings());
        service.Books = [new Book { FileHash = "sha256:abc" }];

        Assert.IsNull(service.FindByHash("sha256:missing"));
    }

    [TestMethod]
    public void FindByUuid_ReturnsMatchingBook()
    {
        var service = new IndexFileService(CreateSettings());
        var uuid = Guid.NewGuid();
        var target = new Book { Uuid = uuid, Title = "Found" };
        service.Books = [new Book { Uuid = Guid.NewGuid(), Title = "Other" }, target];

        var found = service.FindByUuid(uuid);
        Assert.IsNotNull(found);
        Assert.AreEqual("Found", found.Title);
    }

    [TestMethod]
    public void FindByUuid_NoMatch_ReturnsNull()
    {
        var service = new IndexFileService(CreateSettings());
        service.Books = [new Book { Uuid = Guid.NewGuid() }];

        Assert.IsNull(service.FindByUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task SaveAndLoad_MultipleBooks_PreservesAll()
    {
        var service = new IndexFileService(CreateSettings());
        service.Books =
        [
            new Book { Uuid = Guid.NewGuid(), FileHash = "sha256:aaa", Title = "Book A" },
            new Book { Uuid = Guid.NewGuid(), FileHash = "sha256:bbb", Title = "Book B" },
            new Book { Uuid = Guid.NewGuid(), FileHash = "sha256:ccc", Title = "Book C" }
        ];

        await service.SaveAsync();

        var loaded = new IndexFileService(CreateSettings());
        await loaded.LoadAsync();

        Assert.AreEqual(3, loaded.Books.Count);
        Assert.AreEqual("Book A", loaded.Books[0].Title);
        Assert.AreEqual("Book B", loaded.Books[1].Title);
        Assert.AreEqual("Book C", loaded.Books[2].Title);
    }
}
