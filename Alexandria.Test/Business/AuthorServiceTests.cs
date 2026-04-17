using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.BusinessLayer;

[TestClass]
public sealed class AuthorServiceTests
{
    private FakeAuthorRepository authorRepository = null!;
    private FakeBookService bookService = null!;
    private FakeIndexerService indexerService = null!;
    private AuthorService service = null!;

    [TestInitialize]
    public void Setup()
    {
        authorRepository = new FakeAuthorRepository();
        bookService = new FakeBookService();
        indexerService = new FakeIndexerService();
        service = new AuthorService(authorRepository, bookService, indexerService);
    }

    // ---------- GetAuthors ----------

    [TestMethod]
    public async Task GetAuthors_DefaultSort_ByNameAsc()
    {
        var zeta = MakeAuthor("Zeta");
        var alpha = MakeAuthor("Alpha");
        var mu = MakeAuthor("Mu");
        authorRepository.Seed(zeta, alpha, mu);
        bookService.Seed(
            MakeBookFor(alpha),
            MakeBookFor(mu),
            MakeBookFor(zeta));

        var result = await service.GetAuthors();

        Assert.HasCount(3, result);
        Assert.AreEqual("Alpha", result[0].Name);
        Assert.AreEqual("Mu", result[1].Name);
        Assert.AreEqual("Zeta", result[2].Name);
    }

    [TestMethod]
    public async Task GetAuthors_SortByFirstName_OrdersByFirstWord()
    {
        var a1 = MakeAuthor("Charlie Xavier");
        var a2 = MakeAuthor("Bob Alpha");
        var a3 = MakeAuthor("Alice Zeta");
        authorRepository.Seed(a1, a2, a3);
        bookService.Seed(MakeBookFor(a1), MakeBookFor(a2), MakeBookFor(a3));

        var result = await service.GetAuthors(new AuthorSortOptions(AuthorSortField.FirstName, SortOrder.Asc));

        Assert.AreEqual("Alice Zeta", result[0].Name);
        Assert.AreEqual("Bob Alpha", result[1].Name);
        Assert.AreEqual("Charlie Xavier", result[2].Name);
    }

    [TestMethod]
    public async Task GetAuthors_SortByNumberOfBooksDesc_OrdersByBookCount()
    {
        var prolific = MakeAuthor("Prolific");
        var sparse = MakeAuthor("Sparse");
        authorRepository.Seed(prolific, sparse);
        bookService.Seed(
            MakeBookFor(prolific),
            MakeBookFor(prolific),
            MakeBookFor(prolific),
            MakeBookFor(sparse));

        var result = await service.GetAuthors(new AuthorSortOptions(AuthorSortField.NumberOfBooks, SortOrder.Desc));

        Assert.AreEqual("Prolific", result[0].Name);
        Assert.AreEqual("Sparse", result[1].Name);
    }

    [TestMethod]
    public async Task GetAuthors_SortByRecent_OrdersByLastModifiedBook()
    {
        var a1 = MakeAuthor("A1");
        var a2 = MakeAuthor("A2");
        authorRepository.Seed(a1, a2);
        var oldBook = MakeBookFor(a1);
        oldBook.ModifiedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var newBook = MakeBookFor(a2);
        newBook.ModifiedAt = DateTimeOffset.UtcNow;
        bookService.Seed(oldBook, newBook);

        var result = await service.GetAuthors(new AuthorSortOptions(AuthorSortField.Recent, SortOrder.Desc));

        Assert.AreEqual("A2", result[0].Name);
        Assert.AreEqual("A1", result[1].Name);
    }

    [TestMethod]
    public async Task GetAuthors_FiltersOutAuthorsWithNoBooks()
    {
        var active = MakeAuthor("Active");
        var orphan = MakeAuthor("Orphan");
        authorRepository.Seed(active, orphan);
        bookService.Seed(MakeBookFor(active));

        var result = await service.GetAuthors();

        Assert.HasCount(1, result);
        Assert.AreEqual("Active", result[0].Name);
    }

    // ---------- GetAuthorByUuid ----------

    [TestMethod]
    public async Task GetAuthorByUuid_Found_PopulatesBooksSortedByTitleAscByDefault()
    {
        var author = MakeAuthor("Target");
        authorRepository.Seed(author);
        bookService.Seed(
            MakeBookFor(author, title: "Zeta Book"),
            MakeBookFor(author, title: "Alpha Book"));

        var result = await service.GetAuthorByUuid(author.AuthorUuid);

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.Books);
        Assert.AreEqual("Alpha Book", result.Books[0].Title);
        Assert.AreEqual("Zeta Book", result.Books[1].Title);
    }

    [TestMethod]
    public async Task GetAuthorByUuid_AppliesBookSortOption()
    {
        var author = MakeAuthor("Target");
        authorRepository.Seed(author);
        bookService.Seed(
            MakeBookFor(author, title: "Alpha"),
            MakeBookFor(author, title: "Zeta"));

        var result = await service.GetAuthorByUuid(author.AuthorUuid, new BookSortOptions(BookSortField.Title, SortOrder.Desc));

        Assert.IsNotNull(result);
        Assert.AreEqual("Zeta", result.Books[0].Title);
        Assert.AreEqual("Alpha", result.Books[1].Title);
    }

    [TestMethod]
    public async Task GetAuthorByUuid_NotFound_ReturnsNull()
    {
        var result = await service.GetAuthorByUuid(Guid.NewGuid());

        Assert.IsNull(result);
    }

    // ---------- GetAuthorByName ----------

    [TestMethod]
    public async Task GetAuthorByName_Found_PopulatesBooks()
    {
        var author = MakeAuthor("Isaac Asimov");
        authorRepository.Seed(author);
        bookService.Seed(MakeBookFor(author, title: "Foundation"));

        var result = await service.GetAuthorByName("Isaac Asimov");

        Assert.IsNotNull(result);
        Assert.AreEqual(author.AuthorUuid, result.AuthorUuid);
        Assert.HasCount(1, result.Books);
        Assert.AreEqual("Foundation", result.Books[0].Title);
    }

    [TestMethod]
    public async Task GetAuthorByName_NotFound_ReturnsNull()
    {
        var result = await service.GetAuthorByName("Unknown Person");

        Assert.IsNull(result);
    }

    // ---------- UpdateAuthor ----------

    [TestMethod]
    public async Task UpdateAuthor_Found_UpdatesRepositoryAndReindexesAllOwnedBooks()
    {
        var author = MakeAuthor("Old Name");
        authorRepository.Seed(author);
        var book1 = MakeBookFor(author);
        var book2 = MakeBookFor(author);
        bookService.Seed(book1, book2);

        author.Name = "New Name";
        var result = await service.UpdateAuthor(author);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, authorRepository.UpdateCallCount);
        Assert.HasCount(2, indexerService.ReindexCalls);
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == book1.BookUuid));
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == book2.BookUuid));
    }

    [TestMethod]
    public async Task UpdateAuthor_NotFound_ReturnsNullAndDoesNotReindex()
    {
        var author = MakeAuthor("Ghost");

        var result = await service.UpdateAuthor(author);

        Assert.IsNull(result);
        Assert.AreEqual(0, authorRepository.UpdateCallCount);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- MergeAuthor ----------

    [TestMethod]
    public async Task MergeAuthor_CallsReassignAndRemoveOrphans_ReindexesAffected()
    {
        var source = MakeAuthor("Source");
        var target = MakeAuthor("Target");
        authorRepository.Seed(source, target);
        var bookA = MakeBookFor(source);
        var bookB = MakeBookFor(source);
        var unaffected = MakeBookFor(target);
        bookService.Seed(bookA, bookB, unaffected);

        var result = await service.MergeAuthor(source.AuthorUuid, target.AuthorUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(target.AuthorUuid, result.AuthorUuid);
        Assert.HasCount(1, authorRepository.ReassignCalls);
        Assert.AreEqual(source.AuthorUuid, authorRepository.ReassignCalls[0].Source);
        Assert.AreEqual(target.AuthorUuid, authorRepository.ReassignCalls[0].Target);
        Assert.AreEqual(1, authorRepository.RemoveOrphanedCallCount);
        Assert.HasCount(2, indexerService.ReindexCalls);
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == bookA.BookUuid));
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == bookB.BookUuid));
        Assert.IsFalse(indexerService.ReindexCalls.Any(c => c.Uuid == unaffected.BookUuid));
    }

    [TestMethod]
    public async Task MergeAuthor_NoBooksOnSource_StillCallsRepositoryButSkipsReindex()
    {
        var source = MakeAuthor("Source");
        var target = MakeAuthor("Target");
        authorRepository.Seed(source, target);
        bookService.Seed(MakeBookFor(target));

        await service.MergeAuthor(source.AuthorUuid, target.AuthorUuid);

        Assert.HasCount(1, authorRepository.ReassignCalls);
        Assert.AreEqual(1, authorRepository.RemoveOrphanedCallCount);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- Helpers ----------

    private static Author MakeAuthor(string name) => new()
    {
        AuthorUuid = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        ModifiedAt = DateTimeOffset.UtcNow
    };

    private static Book MakeBookFor(Author author, string? title = null)
    {
        var uuid = Guid.NewGuid();
        var effectiveTitle = title ?? $"Book-{uuid.ToString()[..8]}";
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = effectiveTitle,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = author.AuthorUuid, Name = author.Name }],
            File = new BookFile { BookUuid = uuid, SourcePath = $"/tmp/{uuid}.epub", MediaType = MediaType.Ebook }
        };
    }

    // ---------- Fakes ----------

    internal sealed class FakeAuthorRepository : IAuthorRepository
    {
        private readonly Dictionary<Guid, Author> byUuid = [];

        public int UpdateCallCount { get; private set; }
        public int RemoveOrphanedCallCount { get; private set; }
        public List<(Guid Source, Guid Target)> ReassignCalls { get; } = [];

        public void Seed(params Author[] authors)
        {
            foreach (var author in authors)
                byUuid[author.AuthorUuid] = author;
        }

        public Task<List<Author>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Author?> FindByUuid(Guid authorUuid) => Task.FromResult(byUuid.GetValueOrDefault(authorUuid));
        public Task<Author?> FindByName(string name) => Task.FromResult(byUuid.Values.FirstOrDefault(a => a.Name == name));

        public Task<Guid> GetOrCreate(string name)
        {
            var existing = byUuid.Values.FirstOrDefault(a => a.Name == name);
            if (existing is not null)
                return Task.FromResult(existing.AuthorUuid);
            var author = new Author { AuthorUuid = Guid.NewGuid(), Name = name };
            byUuid[author.AuthorUuid] = author;
            return Task.FromResult(author.AuthorUuid);
        }

        public Task SetBookAuthors(Guid bookUuid, List<string> authorNames) => Task.CompletedTask;
        public Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid) => Task.FromResult(new List<Author>());

        public Task RemoveOrphanedAuthors()
        {
            RemoveOrphanedCallCount++;
            return Task.CompletedTask;
        }

        public Task Update(Author author)
        {
            UpdateCallCount++;
            byUuid[author.AuthorUuid] = author;
            return Task.CompletedTask;
        }

        public Task ReassignBooks(Guid sourceUuid, Guid targetUuid)
        {
            ReassignCalls.Add((sourceUuid, targetUuid));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeBookService : IBookService
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

    internal sealed class FakeIndexerService : IIndexerService
    {
        public List<(Guid Uuid, Book? DesiredMetadata, CoverUpdate? CoverUpdate)> ReindexCalls { get; } = [];

        public Task<int> Index() => Task.FromResult(0);

        public Task ReindexBook(Guid uuid, Book? desiredMetadata = null, CoverUpdate? coverUpdate = null)
        {
            ReindexCalls.Add((uuid, desiredMetadata, coverUpdate));
            return Task.CompletedTask;
        }
    }
}
