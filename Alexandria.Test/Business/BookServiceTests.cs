using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class BookServiceTests
{
    private FakeBookRepository bookRepository = null!;
    private FakeReadingStateRepository readingStateRepository = null!;
    private FakeFileContentManager contentManager = null!;
    private FakeIndexerService indexerService = null!;
    private BookService service = null!;

    [TestInitialize]
    public void Setup()
    {
        bookRepository = new FakeBookRepository();
        readingStateRepository = new FakeReadingStateRepository();
        contentManager = new FakeFileContentManager();
        indexerService = new FakeIndexerService();
        service = new BookService(bookRepository, readingStateRepository, contentManager, indexerService);
    }

    // ---------- GetBooks ----------

    [TestMethod]
    public async Task GetBooks_DefaultSort_ByRecentDescending()
    {
        var older = MakeBook("Older", modifiedAt: DateTimeOffset.UtcNow.AddDays(-10));
        var newer = MakeBook("Newer", modifiedAt: DateTimeOffset.UtcNow);
        bookRepository.Seed(older, newer);

        var result = await service.GetBooks();

        Assert.HasCount(2, result);
        Assert.AreEqual("Newer", result[0].Title);
        Assert.AreEqual("Older", result[1].Title);
    }

    [TestMethod]
    public async Task GetBooks_SortByTitleAsc_OrdersAlphabetically()
    {
        bookRepository.Seed(MakeBook("Zeta"), MakeBook("Alpha"), MakeBook("Mu"));

        var result = await service.GetBooks(new BookSortOptions { Sort = BookSortField.Title, Order = SortOrder.Asc });

        Assert.AreEqual("Alpha", result[0].Title);
        Assert.AreEqual("Mu", result[1].Title);
        Assert.AreEqual("Zeta", result[2].Title);
    }

    [TestMethod]
    public async Task GetBooks_SortByDateAddedDesc_OrdersNewestFirst()
    {
        var a = MakeBook("A", createdAt: DateTimeOffset.UtcNow.AddDays(-5));
        var b = MakeBook("B", createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        var c = MakeBook("C", createdAt: DateTimeOffset.UtcNow.AddDays(-3));
        bookRepository.Seed(a, b, c);

        var result = await service.GetBooks(new BookSortOptions { Sort = BookSortField.DateAdded, Order = SortOrder.Desc });

        Assert.AreEqual("B", result[0].Title);
        Assert.AreEqual("C", result[1].Title);
        Assert.AreEqual("A", result[2].Title);
    }

    [TestMethod]
    public async Task GetBooks_SortByFileSize_OrdersBySize()
    {
        var small = MakeBook("Small", fileSize: 100);
        var large = MakeBook("Large", fileSize: 9000);
        bookRepository.Seed(small, large);

        var asc = await service.GetBooks(new BookSortOptions { Sort = BookSortField.FileSize, Order = SortOrder.Asc });
        var desc = await service.GetBooks(new BookSortOptions { Sort = BookSortField.FileSize, Order = SortOrder.Desc });

        Assert.AreEqual("Small", asc[0].Title);
        Assert.AreEqual("Large", desc[0].Title);
    }

    [TestMethod]
    public async Task GetBooks_EnrichesReadingProgressFromReadingStateRepository()
    {
        var book = MakeBook("Under Read");
        bookRepository.Seed(book);
        readingStateRepository.SeedProgress(book.BookUuid, new ReadingProgress { Status = ReadingStatus.Reading, ProgressPercent = 42 });

        var result = await service.GetBooks();

        Assert.HasCount(1, result);
        Assert.IsNotNull(result[0].ReadingProgress);
        Assert.AreEqual(ReadingStatus.Reading, result[0].ReadingProgress!.Status);
        Assert.AreEqual(42m, result[0].ReadingProgress!.ProgressPercent);
    }

    // ---------- GetBookByUuid ----------

    [TestMethod]
    public async Task GetBookByUuid_Found_ReturnsBookWithReadingProgress()
    {
        var book = MakeBook("Found");
        bookRepository.Seed(book);
        readingStateRepository.SeedProgress(book.BookUuid, new ReadingProgress { Status = ReadingStatus.Finished });

        var result = await service.GetBookByUuid(book.BookUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(book.BookUuid, result.BookUuid);
        Assert.AreEqual(ReadingStatus.Finished, result.ReadingProgress!.Status);
    }

    [TestMethod]
    public async Task GetBookByUuid_NotFound_ReturnsNull()
    {
        var result = await service.GetBookByUuid(Guid.NewGuid());

        Assert.IsNull(result);
    }

    // ---------- GetBookByEntitlementUuid ----------

    [TestMethod]
    public async Task GetBookByEntitlementUuid_Found_ReturnsBook()
    {
        var book = MakeBook("ByEntitlement");
        bookRepository.Seed(book);

        var result = await service.GetBookByEntitlementUuid(book.EntitlementUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(book.BookUuid, result.BookUuid);
    }

    [TestMethod]
    public async Task GetBookByEntitlementUuid_NotFound_ReturnsNull()
    {
        var result = await service.GetBookByEntitlementUuid(Guid.NewGuid());

        Assert.IsNull(result);
    }

    // ---------- GetBookCover ----------

    [TestMethod]
    public async Task GetBookCover_Found_DelegatesToContentManagerWithDimensions()
    {
        var book = MakeBook("WithCover");
        bookRepository.Seed(book);
        contentManager.SeedCover(book.BookUuid, [1, 2, 3, 4]);

        var result = await service.GetBookCover(book.BookUuid, 320, 480);

        Assert.IsNotNull(result);
        Assert.HasCount(4, result);
        var lastCall = contentManager.GetCoverFileCalls[^1];
        Assert.AreEqual(book.BookUuid, lastCall.Book.BookUuid);
        Assert.AreEqual(320, lastCall.Width);
        Assert.AreEqual(480, lastCall.Height);
    }

    [TestMethod]
    public async Task GetBookCover_BookNotFound_ReturnsNullWithoutCallingContentManager()
    {
        var result = await service.GetBookCover(Guid.NewGuid());

        Assert.IsNull(result);
        Assert.IsEmpty(contentManager.GetCoverFileCalls);
    }

    // ---------- SetBookArchiveStatus ----------

    [TestMethod]
    public async Task SetBookArchiveStatus_True_SetsIsArchivedAndUpdatesModifiedAt()
    {
        var original = DateTimeOffset.UtcNow.AddDays(-10);
        var book = MakeBook("Target", modifiedAt: original);
        book.IsArchived = false;
        bookRepository.Seed(book);

        await service.SetBookArchiveStatus(book.BookUuid, true);

        var stored = bookRepository.Find(book.BookUuid);
        Assert.IsNotNull(stored);
        Assert.IsTrue(stored.IsArchived);
        Assert.IsGreaterThan(original, stored.ModifiedAt);
    }

    [TestMethod]
    public async Task SetBookArchiveStatus_False_UnsetsIsArchived()
    {
        var book = MakeBook("Target");
        book.IsArchived = true;
        bookRepository.Seed(book);

        await service.SetBookArchiveStatus(book.BookUuid, false);

        Assert.IsFalse(bookRepository.Find(book.BookUuid)!.IsArchived);
    }

    [TestMethod]
    public async Task SetBookArchiveStatus_BookNotFound_DoesNotThrow()
    {
        await service.SetBookArchiveStatus(Guid.NewGuid(), true);

        Assert.AreEqual(0, bookRepository.UpdateCallCount);
    }

    // ---------- UpdateReadingStatus ----------

    [TestMethod]
    public async Task UpdateReadingStatus_Reading_UpsertsStateWithStatusReading()
    {
        var book = MakeBook("ReadTarget");
        bookRepository.Seed(book);

        await service.UpdateReadingStatus(book.BookUuid, ReadingStatus.Reading);

        var stored = readingStateRepository.FindState(book.BookUuid);
        Assert.IsNotNull(stored);
        Assert.AreEqual("Reading", stored.StatusInfo.Status);
    }

    [TestMethod]
    public async Task UpdateReadingStatus_Finished_SetsProgressPercentTo100()
    {
        var book = MakeBook("FinishedTarget");
        bookRepository.Seed(book);

        await service.UpdateReadingStatus(book.BookUuid, ReadingStatus.Finished);

        var stored = readingStateRepository.FindState(book.BookUuid)!;
        Assert.AreEqual("Finished", stored.StatusInfo.Status);
        Assert.AreEqual(100, Convert.ToInt32(stored.CurrentBookmark.ProgressPercent));
    }

    [TestMethod]
    public async Task UpdateReadingStatus_ReadyToRead_ResetsProgressAndLocation()
    {
        var book = MakeBook("ResetTarget");
        bookRepository.Seed(book);
        readingStateRepository.SeedState(book.BookUuid, new KoboStoreReadingState
        {
            EntitlementId = book.BookUuid.ToString(),
            Created = "2024-01-01T00:00:00Z",
            LastModified = "2024-01-01T00:00:00Z",
            PriorityTimestamp = "2024-01-01T00:00:00Z",
            StatusInfo = new KoboStoreStatusInfo { LastModified = "2024-01-01T00:00:00Z", Status = "Reading" },
            Statistics = new KoboStoreStatistics { LastModified = "2024-01-01T00:00:00Z" },
            CurrentBookmark = new KoboStoreBookmark
            {
                LastModified = "2024-01-01T00:00:00Z",
                ProgressPercent = 55,
                ContentSourceProgressPercent = 55,
                Location = new KoboStoreBookmarkLocation { Type = "Text", Value = "somewhere", Source = "source" }
            }
        });

        await service.UpdateReadingStatus(book.BookUuid, ReadingStatus.ReadyToRead);

        var stored = readingStateRepository.FindState(book.BookUuid)!;
        Assert.AreEqual("ReadyToRead", stored.StatusInfo.Status);
        Assert.AreEqual(0, Convert.ToInt32(stored.CurrentBookmark.ProgressPercent));
        Assert.AreEqual(0, Convert.ToInt32(stored.CurrentBookmark.ContentSourceProgressPercent));
        Assert.IsNull(stored.CurrentBookmark.Location);
    }

    [TestMethod]
    public async Task UpdateReadingStatus_UpdatesBookModifiedAt()
    {
        var originalModified = DateTimeOffset.UtcNow.AddDays(-3);
        var book = MakeBook("ModifyTarget", modifiedAt: originalModified);
        bookRepository.Seed(book);

        await service.UpdateReadingStatus(book.BookUuid, ReadingStatus.Reading);

        var stored = bookRepository.Find(book.BookUuid)!;
        Assert.IsGreaterThan(originalModified, stored.ModifiedAt);
    }

    [TestMethod]
    public async Task UpdateReadingStatus_BookMissing_StillPersistsReadingState()
    {
        var bookUuid = Guid.NewGuid();

        await service.UpdateReadingStatus(bookUuid, ReadingStatus.Reading);

        Assert.IsNotNull(readingStateRepository.FindState(bookUuid));
    }

    // ---------- UpdateBookMetadata ----------

    [TestMethod]
    public async Task UpdateBookMetadata_CallsReindexBookWithDesiredMetadata()
    {
        var book = MakeBook("Original");
        bookRepository.Seed(book);
        var update = MakeBook("Original");
        update.BookUuid = book.BookUuid;
        update.Title = "Updated";

        await service.UpdateBookMetadata(update);

        Assert.HasCount(1, indexerService.ReindexCalls);
        var call = indexerService.ReindexCalls[0];
        Assert.AreEqual(book.BookUuid, call.Uuid);
        Assert.IsNotNull(call.DesiredMetadata);
        Assert.AreEqual("Updated", call.DesiredMetadata.Title);
        Assert.IsNull(call.CoverUpdate);
    }

    [TestMethod]
    public async Task UpdateBookMetadata_BookNotFound_ReturnsNullWithoutReindex()
    {
        var update = MakeBook("Ghost");

        var result = await service.UpdateBookMetadata(update);

        Assert.IsNull(result);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- UpdateBookCover ----------

    [TestMethod]
    public async Task UpdateBookCover_CallsReindexBookWithCoverUpdate()
    {
        var book = MakeBook("CoverTarget");
        bookRepository.Seed(book);
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF };

        await service.UpdateBookCover(book.BookUuid, coverBytes, "image/jpeg");

        Assert.HasCount(1, indexerService.ReindexCalls);
        var call = indexerService.ReindexCalls[0];
        Assert.AreEqual(book.BookUuid, call.Uuid);
        Assert.IsNull(call.DesiredMetadata);
        Assert.IsNotNull(call.CoverUpdate);
        Assert.AreEqual("image/jpeg", call.CoverUpdate.MediaType);
        Assert.IsFalse(call.CoverUpdate.RemoveCover);
        Assert.IsNotNull(call.CoverUpdate.CoverData);
        Assert.HasCount(3, call.CoverUpdate.CoverData);
    }

    [TestMethod]
    public async Task UpdateBookCover_BookNotFound_ReturnsNullWithoutReindex()
    {
        var result = await service.UpdateBookCover(Guid.NewGuid(), [0, 1], "image/jpeg");

        Assert.IsNull(result);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- RemoveBookCover ----------

    [TestMethod]
    public async Task RemoveBookCover_CallsReindexBookWithRemoveCoverTrue()
    {
        var book = MakeBook("CoverRemoval");
        bookRepository.Seed(book);

        await service.RemoveBookCover(book.BookUuid);

        Assert.HasCount(1, indexerService.ReindexCalls);
        var call = indexerService.ReindexCalls[0];
        Assert.IsNotNull(call.CoverUpdate);
        Assert.IsTrue(call.CoverUpdate.RemoveCover);
        Assert.IsNull(call.CoverUpdate.CoverData);
    }

    [TestMethod]
    public async Task RemoveBookCover_BookNotFound_ReturnsNullWithoutReindex()
    {
        var result = await service.RemoveBookCover(Guid.NewGuid());

        Assert.IsNull(result);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- GetRecommendations ----------

    [TestMethod]
    public async Task GetRecommendations_SeriesNextBookPrioritizedOverAuthor()
    {
        var seriesUuid = Guid.NewGuid();
        var authorUuid = Guid.NewGuid();
        var author = new Author { AuthorUuid = authorUuid, Name = "Shared" };

        var source = MakeBook("Book 1", authors: [author]);
        source.Series = new Series { SeriesUuid = seriesUuid, Name = "Series", Index = 1 };

        var nextInSeries = MakeBook("Book 2", authors: [author]);
        nextInSeries.Series = new Series { SeriesUuid = seriesUuid, Name = "Series", Index = 2 };

        var otherByAuthor = MakeBook("Different", authors: [author]);

        bookRepository.Seed(source, nextInSeries, otherByAuthor);

        var result = await service.GetRecommendations(source.BookUuid, count: 5);

        Assert.HasCount(2, result);
        Assert.AreEqual(nextInSeries.BookUuid, result[0].BookUuid);
        Assert.AreEqual(otherByAuthor.BookUuid, result[1].BookUuid);
    }

    [TestMethod]
    public async Task GetRecommendations_AuthorPrioritizedOverSubject()
    {
        var authorA = new Author { AuthorUuid = Guid.NewGuid(), Name = "A" };
        var authorB = new Author { AuthorUuid = Guid.NewGuid(), Name = "B" };
        var source = MakeBook("Source", authors: [authorA]);
        source.Subjects = ["Fantasy"];

        var byAuthor = MakeBook("Author Match", authors: [authorA]);
        var byGenre = MakeBook("Genre Match", authors: [authorB]);
        byGenre.Subjects = ["Fantasy"];

        bookRepository.Seed(source, byAuthor, byGenre);

        var result = await service.GetRecommendations(source.BookUuid, count: 5);

        Assert.HasCount(2, result);
        Assert.AreEqual(byAuthor.BookUuid, result[0].BookUuid);
        Assert.AreEqual(byGenre.BookUuid, result[1].BookUuid);
    }

    [TestMethod]
    public async Task GetRecommendations_ExcludesArchivedBooks()
    {
        var author = new Author { AuthorUuid = Guid.NewGuid(), Name = "Same" };
        var source = MakeBook("Source", authors: [author]);
        var archived = MakeBook("Archived", authors: [author]);
        archived.IsArchived = true;

        bookRepository.Seed(source, archived);

        var result = await service.GetRecommendations(source.BookUuid);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetRecommendations_ExcludesDifferentMediaType()
    {
        var author = new Author { AuthorUuid = Guid.NewGuid(), Name = "Same" };
        var source = MakeBook("EbookSource", authors: [author]);
        source.File.MediaType = MediaType.Ebook;
        var audiobook = MakeBook("Audiobook", authors: [author]);
        audiobook.File.MediaType = MediaType.Audiobook;

        bookRepository.Seed(source, audiobook);

        var result = await service.GetRecommendations(source.BookUuid);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetRecommendations_UnknownBook_ReturnsEmpty()
    {
        var result = await service.GetRecommendations(Guid.NewGuid());

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task GetRecommendations_FillsUpToCountWithRandomBooks()
    {
        // Source has no series, no author matches, no subject matches. Tier 4 (random) must still fill.
        var source = MakeBook("Source");
        var other1 = MakeBook("Other1");
        var other2 = MakeBook("Other2");
        var other3 = MakeBook("Other3");
        bookRepository.Seed(source, other1, other2, other3);

        var result = await service.GetRecommendations(source.BookUuid, count: 2);

        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(b => b.BookUuid != source.BookUuid));
    }

    // ---------- GetPublishers ----------

    [TestMethod]
    public async Task GetPublishers_DelegatesToRepository()
    {
        bookRepository.SeedPublishers("Penguin", "Tor", "Orbit");

        var result = await service.GetPublishers();

        Assert.HasCount(3, result);
        Assert.Contains("Penguin", result);
        Assert.Contains("Tor", result);
        Assert.Contains("Orbit", result);
    }

    // ---------- Helpers ----------

    private static Book MakeBook(string title, List<Author>? authors = null, DateTimeOffset? createdAt = null, DateTimeOffset? modifiedAt = null, long fileSize = 1024)
    {
        var uuid = Guid.NewGuid();
        var effectiveAuthors = authors ?? [new Author { AuthorUuid = Guid.NewGuid(), Name = "Default Author" }];
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ModifiedAt = modifiedAt ?? DateTimeOffset.UtcNow,
            Authors = effectiveAuthors,
            File = new BookFile
            {
                BookUuid = uuid,
                SourcePath = $"/tmp/{title}.epub",
                MediaType = MediaType.Ebook,
                TotalFileSize = fileSize
            }
        };
    }

    // ---------- Fakes ----------

    internal sealed class FakeBookRepository : IBookRepository
    {
        private readonly Dictionary<Guid, Book> byUuid = [];
        private readonly List<string> publishers = [];

        public int UpdateCallCount { get; private set; }

        public void Seed(params Book[] books)
        {
            foreach (var book in books)
                byUuid[book.BookUuid] = book;
        }

        public void SeedPublishers(params string[] values) => publishers.AddRange(values);

        public Book? Find(Guid uuid) => byUuid.GetValueOrDefault(uuid);

        public Task<List<Book>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<List<BookCoverKey>> GetCoverLookupKeys() => Task.FromResult(byUuid.Values.Select(b => new BookCoverKey { BookUuid = b.BookUuid, EntitlementUuid = b.EntitlementUuid, ModifiedAt = b.ModifiedAt }).ToList());
        public Task<Book?> FindByUuid(Guid uuid) => Task.FromResult(byUuid.GetValueOrDefault(uuid));
        public Task<Book?> FindByEntitlementUuid(Guid entitlementUuid) => Task.FromResult(byUuid.Values.FirstOrDefault(b => b.EntitlementUuid == entitlementUuid));
        public Task<Book?> FindByPath(string sourcePath) => Task.FromResult(byUuid.Values.FirstOrDefault(b => string.Equals(b.File.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)));

        public Task Add(Book book)
        {
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Update(Book book)
        {
            UpdateCallCount++;
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Remove(Guid uuid)
        {
            byUuid.Remove(uuid);
            return Task.CompletedTask;
        }

        public Task<List<string>> GetPublishers() => Task.FromResult(publishers.ToList());
    }

    internal sealed class FakeReadingStateRepository : IReadingStateRepository
    {
        private readonly Dictionary<Guid, KoboStoreReadingState> states = [];
        private readonly Dictionary<Guid, ReadingProgress> progress = [];

        public void SeedProgress(Guid bookUuid, ReadingProgress value) => progress[bookUuid] = value;

        public void SeedState(Guid bookUuid, KoboStoreReadingState state) => states[bookUuid] = state;

        public KoboStoreReadingState? FindState(Guid bookUuid) => states.GetValueOrDefault(bookUuid);

        public Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid) => Task.FromResult(states.GetValueOrDefault(bookUuid));

        public Task<ReadingProgress?> GetReadingProgress(Guid bookUuid) => Task.FromResult(progress.GetValueOrDefault(bookUuid));

        public Task<bool> Upsert(Guid bookUuid, KoboStoreReadingState state)
        {
            states[bookUuid] = state;
            return Task.FromResult(true);
        }

        public Task DeleteByBookUuid(Guid bookUuid)
        {
            states.Remove(bookUuid);
            progress.Remove(bookUuid);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeFileContentManager : IFileContentManager
    {
        private readonly Dictionary<Guid, byte[]> covers = [];

        public List<(Book Book, int? Width, int? Height, int? Quality)> GetCoverFileCalls { get; } = [];

        public void SeedCover(Guid bookUuid, byte[] bytes) => covers[bookUuid] = bytes;

        public int GetBookFilesCount() => throw new NotImplementedException();
        public IAsyncEnumerable<(Book Metadata, byte[]? CoverBytes)> GetAllBooks() => throw new NotImplementedException();
        public string? SaveCover(string filePath, byte[]? coverBytes, Guid bookUuid) => throw new NotImplementedException();
        public void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? previousBooks) => throw new NotImplementedException();
        public Task CreateDerivativeFiles(Book book) => throw new NotImplementedException();
        public Task<Book> GetBook(Book book) => throw new NotImplementedException();
        public Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null) => throw new NotImplementedException();
        public Task<byte[]?> GetContentFile(Book book) => throw new NotImplementedException();
        public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null)
        {
            GetCoverFileCalls.Add((book, width, height, quality));
            return Task.FromResult<byte[]?>(covers.GetValueOrDefault(book.BookUuid));
        }
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
