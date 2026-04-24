using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class SeriesServiceTests
{
    private FakeSeriesRepository seriesRepository = null!;
    private FakeBookService bookService = null!;
    private FakeIndexerService indexerService = null!;
    private SeriesService service = null!;

    [TestInitialize]
    public void Setup()
    {
        seriesRepository = new FakeSeriesRepository();
        bookService = new FakeBookService();
        indexerService = new FakeIndexerService();
        service = new SeriesService(seriesRepository, bookService, indexerService);
    }

    // ---------- GetSeries ----------

    [TestMethod]
    public async Task GetSeries_DefaultSort_ByNameAsc()
    {
        var zeta = MakeSeries("Zeta");
        var alpha = MakeSeries("Alpha");
        seriesRepository.Seed(zeta, alpha);
        bookService.Seed(MakeBookForSeries(alpha), MakeBookForSeries(zeta));

        var result = await service.GetSeries();

        Assert.HasCount(2, result);
        Assert.AreEqual("Alpha", result[0].Name);
        Assert.AreEqual("Zeta", result[1].Name);
    }

    [TestMethod]
    public async Task GetSeries_SortByNumberOfBooksDesc_OrdersByBookCount()
    {
        var big = MakeSeries("Big");
        var small = MakeSeries("Small");
        seriesRepository.Seed(big, small);
        bookService.Seed(
            MakeBookForSeries(big, index: 1),
            MakeBookForSeries(big, index: 2),
            MakeBookForSeries(big, index: 3),
            MakeBookForSeries(small, index: 1));

        var result = await service.GetSeries(new SeriesSortOptions { Sort = SeriesSortField.NumberOfBooks, Order = SortOrder.Desc });

        Assert.AreEqual("Big", result[0].Name);
        Assert.AreEqual("Small", result[1].Name);
    }

    [TestMethod]
    public async Task GetSeries_SortByRecent_OrdersByLastModifiedBook()
    {
        var s1 = MakeSeries("S1");
        var s2 = MakeSeries("S2");
        seriesRepository.Seed(s1, s2);
        var older = MakeBookForSeries(s1);
        older.ModifiedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var newer = MakeBookForSeries(s2);
        newer.ModifiedAt = DateTimeOffset.UtcNow;
        bookService.Seed(older, newer);

        var result = await service.GetSeries(new SeriesSortOptions { Sort = SeriesSortField.Recent, Order = SortOrder.Desc });

        Assert.AreEqual("S2", result[0].Name);
        Assert.AreEqual("S1", result[1].Name);
    }

    [TestMethod]
    public async Task GetSeries_FiltersOutSeriesWithNoBooks()
    {
        var active = MakeSeries("Active");
        var orphan = MakeSeries("Orphan");
        seriesRepository.Seed(active, orphan);
        bookService.Seed(MakeBookForSeries(active));

        var result = await service.GetSeries();

        Assert.HasCount(1, result);
        Assert.AreEqual("Active", result[0].Name);
    }

    // ---------- GetSeriesByUuid ----------

    [TestMethod]
    public async Task GetSeriesByUuid_Found_PopulatesBooksOrderedByIndex()
    {
        var series = MakeSeries("Target");
        seriesRepository.Seed(series);
        bookService.Seed(
            MakeBookForSeries(series, index: 3, title: "Three"),
            MakeBookForSeries(series, index: 1, title: "One"),
            MakeBookForSeries(series, index: 2, title: "Two"));

        var result = await service.GetSeriesByUuid(series.SeriesUuid);

        Assert.IsNotNull(result);
        Assert.HasCount(3, result.Books);
        Assert.AreEqual("One", result.Books[0].Title);
        Assert.AreEqual("Two", result.Books[1].Title);
        Assert.AreEqual("Three", result.Books[2].Title);
    }

    [TestMethod]
    public async Task GetSeriesByUuid_AppliesBookSortOption()
    {
        var series = MakeSeries("Target");
        seriesRepository.Seed(series);
        bookService.Seed(
            MakeBookForSeries(series, index: 1, title: "Alpha"),
            MakeBookForSeries(series, index: 2, title: "Zeta"));

        var asc = await service.GetSeriesByUuid(series.SeriesUuid, new BookSortOptions { Sort = BookSortField.Title, Order = SortOrder.Asc });
        Assert.IsNotNull(asc);
        Assert.AreEqual("Alpha", asc.Books[0].Title);
        Assert.AreEqual("Zeta", asc.Books[1].Title);

        var desc = await service.GetSeriesByUuid(series.SeriesUuid, new BookSortOptions { Sort = BookSortField.Title, Order = SortOrder.Desc });
        Assert.IsNotNull(desc);
        Assert.AreEqual("Zeta", desc.Books[0].Title);
        Assert.AreEqual("Alpha", desc.Books[1].Title);
    }

    [TestMethod]
    public async Task GetSeriesByUuid_NotFound_ReturnsNull()
    {
        var result = await service.GetSeriesByUuid(Guid.NewGuid());

        Assert.IsNull(result);
    }

    // ---------- GetSeriesByName ----------

    [TestMethod]
    public async Task GetSeriesByName_Found_ReturnsSeries()
    {
        var series = MakeSeries("Foundation");
        seriesRepository.Seed(series);

        var result = await service.GetSeriesByName("Foundation");

        Assert.IsNotNull(result);
        Assert.AreEqual(series.SeriesUuid, result.SeriesUuid);
    }

    [TestMethod]
    public async Task GetSeriesByName_NotFound_ReturnsNull()
    {
        var result = await service.GetSeriesByName("Unknown");

        Assert.IsNull(result);
    }

    // ---------- UpdateSeries ----------

    [TestMethod]
    public async Task UpdateSeries_Found_UpdatesRepositoryAndReindexesAllBooks()
    {
        var series = MakeSeries("Old");
        seriesRepository.Seed(series);
        var book1 = MakeBookForSeries(series, index: 1);
        var book2 = MakeBookForSeries(series, index: 2);
        bookService.Seed(book1, book2);

        series.Name = "New";
        var result = await service.UpdateSeries(series);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, seriesRepository.UpdateCallCount);
        Assert.HasCount(2, indexerService.ReindexCalls);
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == book1.BookUuid));
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == book2.BookUuid));
    }

    [TestMethod]
    public async Task UpdateSeries_NotFound_ReturnsNullAndDoesNotReindex()
    {
        var series = MakeSeries("Ghost");

        var result = await service.UpdateSeries(series);

        Assert.IsNull(result);
        Assert.AreEqual(0, seriesRepository.UpdateCallCount);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- MergeSeries ----------

    [TestMethod]
    public async Task MergeSeries_CallsReassignAndRemoveOrphans_ReindexesAffected()
    {
        var source = MakeSeries("Source");
        var target = MakeSeries("Target");
        seriesRepository.Seed(source, target);
        var bookA = MakeBookForSeries(source, index: 1);
        var bookB = MakeBookForSeries(source, index: 2);
        var unaffected = MakeBookForSeries(target, index: 1);
        bookService.Seed(bookA, bookB, unaffected);

        var result = await service.MergeSeries(source.SeriesUuid, target.SeriesUuid);

        Assert.IsNotNull(result);
        Assert.AreEqual(target.SeriesUuid, result.SeriesUuid);
        Assert.HasCount(1, seriesRepository.ReassignCalls);
        Assert.AreEqual(source.SeriesUuid, seriesRepository.ReassignCalls[0].Source);
        Assert.AreEqual(target.SeriesUuid, seriesRepository.ReassignCalls[0].Target);
        Assert.AreEqual(1, seriesRepository.RemoveOrphanedCallCount);
        Assert.HasCount(2, indexerService.ReindexCalls);
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == bookA.BookUuid));
        Assert.IsTrue(indexerService.ReindexCalls.Any(c => c.Uuid == bookB.BookUuid));
        Assert.IsFalse(indexerService.ReindexCalls.Any(c => c.Uuid == unaffected.BookUuid));
    }

    [TestMethod]
    public async Task MergeSeries_NoBooksOnSource_StillCallsRepositoryButSkipsReindex()
    {
        var source = MakeSeries("Source");
        var target = MakeSeries("Target");
        seriesRepository.Seed(source, target);
        bookService.Seed(MakeBookForSeries(target, index: 1));

        await service.MergeSeries(source.SeriesUuid, target.SeriesUuid);

        Assert.HasCount(1, seriesRepository.ReassignCalls);
        Assert.AreEqual(1, seriesRepository.RemoveOrphanedCallCount);
        Assert.IsEmpty(indexerService.ReindexCalls);
    }

    // ---------- Helpers ----------

    private static Series MakeSeries(string name) => new()
    {
        SeriesUuid = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        ModifiedAt = DateTimeOffset.UtcNow
    };

    private static Book MakeBookForSeries(Series series, decimal? index = null, string? title = null)
    {
        var uuid = Guid.NewGuid();
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title ?? $"Book-{uuid.ToString()[..8]}",
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Series = new Series { SeriesUuid = series.SeriesUuid, Name = series.Name, Index = index },
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Any Author" }],
            File = new BookFile { BookUuid = uuid, SourcePath = $"/tmp/{uuid}.epub", MediaType = MediaType.Ebook }
        };
    }

    // ---------- Fakes ----------

    internal sealed class FakeSeriesRepository : ISeriesRepository
    {
        private readonly Dictionary<Guid, Series> byUuid = [];

        public int UpdateCallCount { get; private set; }
        public int RemoveOrphanedCallCount { get; private set; }
        public List<(Guid Source, Guid Target)> ReassignCalls { get; } = [];

        public void Seed(params Series[] series)
        {
            foreach (var s in series)
                byUuid[s.SeriesUuid] = s;
        }

        public Task<List<Series>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Series?> FindByUuid(Guid seriesUuid) => Task.FromResult(byUuid.GetValueOrDefault(seriesUuid));
        public Task<Series?> FindByName(string name) => Task.FromResult(byUuid.Values.FirstOrDefault(s => s.Name == name));

        public Task<Guid> GetOrCreate(string name)
        {
            var existing = byUuid.Values.FirstOrDefault(s => s.Name == name);
            if (existing is not null)
                return Task.FromResult(existing.SeriesUuid);
            var series = new Series { SeriesUuid = Guid.NewGuid(), Name = name };
            byUuid[series.SeriesUuid] = series;
            return Task.FromResult(series.SeriesUuid);
        }

        public Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index) => Task.CompletedTask;
        public Task RemoveBookSeries(Guid bookUuid) => Task.CompletedTask;
        public Task<Series?> GetSeriesByBookUuid(Guid bookUuid) => Task.FromResult<Series?>(null);

        public Task RemoveOrphanedSeries()
        {
            RemoveOrphanedCallCount++;
            return Task.CompletedTask;
        }

        public Task Update(Series series)
        {
            UpdateCallCount++;
            byUuid[series.SeriesUuid] = series;
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
        public Task<List<BookCoverKey>> GetCoverLookupKeys() => Task.FromResult(books.Select(b => new BookCoverKey { BookUuid = b.BookUuid, EntitlementUuid = b.EntitlementUuid, ModifiedAt = b.ModifiedAt }).ToList());

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
