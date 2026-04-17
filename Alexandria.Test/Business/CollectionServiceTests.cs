using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.BusinessLayer;

[TestClass]
public sealed class CollectionServiceTests
{
    private FakeCollectionRepository collectionRepository = null!;
    private FakeBookService bookService = null!;
    private CollectionService service = null!;

    [TestInitialize]
    public void Setup()
    {
        collectionRepository = new FakeCollectionRepository();
        bookService = new FakeBookService();
        service = new CollectionService(collectionRepository, bookService);
    }

    [TestMethod]
    public async Task GetCollections_DefaultSort_SortedByNameAsc()
    {
        collectionRepository.Seed(
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Zeta" },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Alpha" },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Mu" });

        var result = await service.GetCollections();

        Assert.HasCount(3, result);
        Assert.AreEqual("Alpha", result[0].Name);
        Assert.AreEqual("Mu", result[1].Name);
        Assert.AreEqual("Zeta", result[2].Name);
    }

    [TestMethod]
    public async Task GetCollections_SortByNumberOfBooksDesc_ReturnsLargestFirst()
    {
        collectionRepository.Seed(
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Small", BookUuids = [Guid.NewGuid()] },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Big", BookUuids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()] },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Medium", BookUuids = [Guid.NewGuid(), Guid.NewGuid()] });

        var result = await service.GetCollections(new CollectionSortOptions(CollectionSortField.NumberOfBooks, SortOrder.Desc));

        Assert.AreEqual("Big", result[0].Name);
        Assert.AreEqual("Medium", result[1].Name);
        Assert.AreEqual("Small", result[2].Name);
    }

    [TestMethod]
    public async Task GetCollections_SortByDateCreatedDesc_ReturnsNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        collectionRepository.Seed(
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Old", CreatedAt = now.AddDays(-10) },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "New", CreatedAt = now },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Mid", CreatedAt = now.AddDays(-5) });

        var result = await service.GetCollections(new CollectionSortOptions(CollectionSortField.DateCreated, SortOrder.Desc));

        Assert.AreEqual("New", result[0].Name);
        Assert.AreEqual("Mid", result[1].Name);
        Assert.AreEqual("Old", result[2].Name);
    }

    [TestMethod]
    public async Task GetCollectionWithBooks_FiltersBooksByUuidMembership()
    {
        var bookA = MakeBook("A");
        var bookB = MakeBook("B");
        var bookC = MakeBook("C");
        bookService.Seed(bookA, bookB, bookC);

        var collectionUuid = Guid.NewGuid();
        collectionRepository.Seed(new BookCollection
        {
            CollectionUuid = collectionUuid,
            Name = "Test",
            BookUuids = [bookA.BookUuid, bookC.BookUuid]
        });

        var result = await service.GetCollectionWithBooks(collectionUuid);

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.Books);
        Assert.IsTrue(result.Books.Any(b => b.BookUuid == bookA.BookUuid));
        Assert.IsTrue(result.Books.Any(b => b.BookUuid == bookC.BookUuid));
        Assert.IsFalse(result.Books.Any(b => b.BookUuid == bookB.BookUuid));
    }

    [TestMethod]
    public async Task GetCollectionWithBooks_AppliesBookSortOptions()
    {
        var bookA = MakeBook("Alpha");
        var bookZ = MakeBook("Zeta");
        bookService.Seed(bookA, bookZ);

        var collectionUuid = Guid.NewGuid();
        collectionRepository.Seed(new BookCollection
        {
            CollectionUuid = collectionUuid,
            Name = "Test",
            BookUuids = [bookA.BookUuid, bookZ.BookUuid]
        });

        var asc = await service.GetCollectionWithBooks(collectionUuid, new BookSortOptions(BookSortField.Title, SortOrder.Asc));
        var desc = await service.GetCollectionWithBooks(collectionUuid, new BookSortOptions(BookSortField.Title, SortOrder.Desc));

        Assert.IsNotNull(asc);
        Assert.IsNotNull(desc);
        Assert.AreEqual("Alpha", asc.Books[0].Title);
        Assert.AreEqual("Zeta", desc.Books[0].Title);
    }

    [TestMethod]
    public async Task GetCollectionWithBooks_NotFound_ReturnsNull()
    {
        var result = await service.GetCollectionWithBooks(Guid.NewGuid());

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetCollectionWithBooks_CopiesNameAndTimestampsFromCollection()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-3);
        var modified = DateTimeOffset.UtcNow;
        var uuid = Guid.NewGuid();
        collectionRepository.Seed(new BookCollection
        {
            CollectionUuid = uuid,
            Name = "Shelf Name",
            CreatedAt = created,
            ModifiedAt = modified,
            BookUuids = []
        });

        var result = await service.GetCollectionWithBooks(uuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(uuid, result.CollectionUuid);
        Assert.AreEqual("Shelf Name", result.Name);
        Assert.AreEqual(created, result.CreatedAt);
        Assert.AreEqual(modified, result.ModifiedAt);
        Assert.IsEmpty(result.Books);
    }

    [TestMethod]
    public async Task UpsertCollection_DelegatesToRepository()
    {
        var collection = new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "New" };

        await service.UpsertCollection(collection);

        Assert.AreEqual(1, collectionRepository.UpsertCallCount);
        Assert.IsTrue(collectionRepository.All.Any(c => c.CollectionUuid == collection.CollectionUuid));
    }

    [TestMethod]
    public async Task GetCollectionsModifiedSince_ReturnsRepositoryResult()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        collectionRepository.Seed(
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Stale", ModifiedAt = cutoff.AddDays(-5) },
            new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Fresh", ModifiedAt = cutoff.AddMinutes(1) });

        var result = await service.GetCollectionsModifiedSince(cutoff);

        Assert.HasCount(1, result);
        Assert.AreEqual("Fresh", result[0].Name);
    }

    private static Book MakeBook(string title)
    {
        var uuid = Guid.NewGuid();
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            File = new BookFile { BookUuid = uuid, SourcePath = $"/tmp/{title}.epub" }
        };
    }

    private sealed class FakeCollectionRepository : ICollectionRepository
    {
        private readonly Dictionary<Guid, BookCollection> store = [];
        public int UpsertCallCount { get; private set; }

        public IEnumerable<BookCollection> All => store.Values;

        public void Seed(params BookCollection[] collections)
        {
            foreach (var collection in collections)
                store[collection.CollectionUuid] = collection;
        }

        public Task<List<BookCollection>> GetAll() => Task.FromResult(store.Values.ToList());

        public Task<BookCollection?> FindByUuid(Guid uuid) => Task.FromResult(store.GetValueOrDefault(uuid));

        public Task Upsert(BookCollection collection)
        {
            UpsertCallCount++;
            store[collection.CollectionUuid] = collection;
            return Task.CompletedTask;
        }

        public Task Delete(Guid uuid)
        {
            store.Remove(uuid);
            return Task.CompletedTask;
        }

        public Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since) => Task.FromResult(store.Values.Where(c => c.ModifiedAt > since).ToList());
    }

    private sealed class FakeBookService : IBookService
    {
        private readonly List<Book> books = [];

        public void Seed(params Book[] seed) => books.AddRange(seed);

        public Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null) => Task.FromResult(books.ToList());

        public Task<Book?> GetBookByUuid(Guid uuid) => throw new NotImplementedException();
        public Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid) => throw new NotImplementedException();
        public Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null) => throw new NotImplementedException();
        public Task SetBookArchiveStatus(Guid uuid, bool isArchived) => throw new NotImplementedException();
        public Task UpdateReadingStatus(Guid uuid, ReadingStatus status) => throw new NotImplementedException();
        public Task<Book?> UpdateBookMetadata(Book book) => throw new NotImplementedException();
        public Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType) => throw new NotImplementedException();
        public Task<Book?> RemoveBookCover(Guid uuid) => throw new NotImplementedException();
        public Task<List<Book>> GetRecommendations(Guid bookUuid, int count = 5) => throw new NotImplementedException();
        public Task<List<string>> GetPublishers() => throw new NotImplementedException();
    }
}
