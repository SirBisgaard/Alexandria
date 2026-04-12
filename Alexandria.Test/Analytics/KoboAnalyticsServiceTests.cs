using System.Net;
using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Alexandria.Business.KoboStoreProxy.Services;
using Alexandria.Business.Library.Models.Others;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Analytics;

[TestClass]
public sealed class KoboAnalyticsServiceTests
{
    private static Book MakeBook(string title, MediaType mediaType = MediaType.Audiobook)
    {
        var uuid = Guid.NewGuid();
        return new Book { BookUuid = uuid, EntitlementUuid = uuid, Title = title, Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Test Author" }], Language = "en", SourcePath = "/test.m4b", TotalFileSize = 1000, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow, MediaType = mediaType };
    }

    private static KoboStoreAnalyticsRequest MakeAnalyticsRequest(params KoboStoreAnalyticsEvent[] events) => new()
    {
        AffiliateName = "Kobo",
        ApplicationVersion = "4.45.23646",
        Events = events.ToList(),
        PlatformId = "00000000-0000-0000-0000-000000000390",
        SerialNumber = "test-serial"
    };

    private static KoboStoreAnalyticsEvent MakeMarkAsFinished(Guid entitlementUuid) => new()
    {
        Attributes = new Dictionary<string, string> { ["Origin"] = "MarkAsFinished", ["Screen"] = "", ["volumeid"] = entitlementUuid.ToString() },
        EventType = "MarkAsFinished",
        Timestamp = "2026-04-12T01:20:31Z"
    };

    private static KoboStoreAnalyticsEvent MakeSyncEvent() => new()
    {
        Attributes = new Dictionary<string, string> { ["Action"] = "Sync" },
        EventType = "StatusBarOption",
        Timestamp = "2026-04-12T01:20:35Z"
    };

    private static KoboStoreAnalyticsEvent MakeUnknownEventWithVolumeId(Guid entitlementUuid) => new()
    {
        Attributes = new Dictionary<string, string> { ["volumeid"] = entitlementUuid.ToString() },
        EventType = "SomeOtherEvent",
        Timestamp = "2026-04-12T01:20:35Z"
    };

    private static KoboProxyRequest MakeRequest(KoboStoreAnalyticsRequest analyticsRequest)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(analyticsRequest);
        return new KoboProxyRequest { Method = "POST", Path = "/v1/analytics/event", QueryString = "", Headers = new Dictionary<string, string?[]>(), Body = body };
    }

    private static KoboProxyRequest MakeRawRequest(byte[] body) => new() { Method = "POST", Path = "/v1/analytics/event", QueryString = "", Headers = new Dictionary<string, string?[]>(), Body = body };

    private static KoboAnalyticsService CreateService(List<Book> books, FakeReadingStateRepository? readingStateRepo = null, FakeProxyService? proxyService = null)
    {
        var libraryService = new FakeLibraryService(books);
        readingStateRepo ??= new FakeReadingStateRepository();
        proxyService ??= new FakeProxyService();
        return new KoboAnalyticsService(proxyService, libraryService, readingStateRepo, NullLogger<KoboAnalyticsService>.Instance);
    }

    [TestMethod]
    public async Task MarkAsFinished_LocalBook_CreatesFinishedReadingState()
    {
        var book = MakeBook("The Alchemyst");
        var readingStateRepo = new FakeReadingStateRepository();
        var service = CreateService([book], readingStateRepo);
        var request = MakeRequest(MakeAnalyticsRequest(MakeMarkAsFinished(book.EntitlementUuid)));

        await service.AnalyticsEvent(request);

        var state = readingStateRepo.GetStored(book.BookUuid);
        Assert.IsNotNull(state, "Reading state should be created");
        Assert.AreEqual("Finished", state.StatusInfo.Status);
        Assert.AreEqual(100, (int)state.CurrentBookmark.ProgressPercent!);
        Assert.AreEqual(1, state.StatusInfo.TimesStartedReading);
    }

    [TestMethod]
    public async Task MarkAsFinished_ExistingReadingState_PreservesTimesStarted()
    {
        var book = MakeBook("The Alchemyst");
        var readingStateRepo = new FakeReadingStateRepository();
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        readingStateRepo.Seed(book.BookUuid, new KoboStoreReadingState
        {
            EntitlementId = book.EntitlementUuid.ToString(),
            Created = now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = now, Status = "Reading", TimesStartedReading = 3, LastTimeStartedReading = now },
            Statistics = new KoboStoreStatistics { LastModified = now, SpentReadingMinutes = 120 },
            CurrentBookmark = new KoboStoreBookmark { LastModified = now, ProgressPercent = 50 }
        });
        var service = CreateService([book], readingStateRepo);
        var request = MakeRequest(MakeAnalyticsRequest(MakeMarkAsFinished(book.EntitlementUuid)));

        await service.AnalyticsEvent(request);

        var state = readingStateRepo.GetStored(book.BookUuid);
        Assert.IsNotNull(state);
        Assert.AreEqual("Finished", state.StatusInfo.Status);
        Assert.AreEqual(3, state.StatusInfo.TimesStartedReading);
        Assert.AreEqual(120, state.Statistics.SpentReadingMinutes);
    }

    [TestMethod]
    public async Task NonLocalBook_EventProxied()
    {
        var proxyService = new FakeProxyService();
        var service = CreateService([], proxyService: proxyService);
        var nonLocalUuid = Guid.NewGuid();
        var request = MakeRequest(MakeAnalyticsRequest(MakeMarkAsFinished(nonLocalUuid)));

        await service.AnalyticsEvent(request);

        Assert.IsTrue(proxyService.WasCalled, "Non-local event should be proxied");
        var proxiedBody = JsonSerializer.Deserialize<KoboStoreAnalyticsRequest>(proxyService.LastRequest!.Body);
        Assert.HasCount(1, proxiedBody!.Events!);
        Assert.AreEqual("MarkAsFinished", proxiedBody.Events![0].EventType);
    }

    [TestMethod]
    public async Task MixedEvents_LocalHandled_NonLocalProxied()
    {
        var localBook = MakeBook("Local Book");
        var nonLocalUuid = Guid.NewGuid();
        var readingStateRepo = new FakeReadingStateRepository();
        var proxyService = new FakeProxyService();
        var service = CreateService([localBook], readingStateRepo, proxyService);
        var events = MakeAnalyticsRequest(MakeMarkAsFinished(localBook.EntitlementUuid), MakeMarkAsFinished(nonLocalUuid), MakeSyncEvent());
        var request = MakeRequest(events);

        await service.AnalyticsEvent(request);

        var state = readingStateRepo.GetStored(localBook.BookUuid);
        Assert.IsNotNull(state, "Local book should have reading state created");
        Assert.AreEqual("Finished", state.StatusInfo.Status);

        Assert.IsTrue(proxyService.WasCalled, "Non-local events should be proxied");
        var proxiedBody = JsonSerializer.Deserialize<KoboStoreAnalyticsRequest>(proxyService.LastRequest!.Body);
        Assert.HasCount(2, proxiedBody!.Events!);
    }

    [TestMethod]
    public async Task AllEventsLocal_NoProxyCall()
    {
        var book = MakeBook("Local Book");
        var proxyService = new FakeProxyService();
        var service = CreateService([book], proxyService: proxyService);
        var request = MakeRequest(MakeAnalyticsRequest(MakeMarkAsFinished(book.EntitlementUuid)));

        await service.AnalyticsEvent(request);

        Assert.IsFalse(proxyService.WasCalled, "All-local events should not trigger proxy");
    }

    [TestMethod]
    public async Task MalformedBody_ProxiedUnchanged()
    {
        var proxyService = new FakeProxyService();
        var service = CreateService([], proxyService: proxyService);
        var request = MakeRawRequest(Encoding.UTF8.GetBytes("not json"));

        await service.AnalyticsEvent(request);

        Assert.IsTrue(proxyService.WasCalled, "Malformed body should be proxied");
    }

    [TestMethod]
    public async Task UnknownEventType_LocalBook_NotProxied()
    {
        var book = MakeBook("Local Book");
        var proxyService = new FakeProxyService();
        var readingStateRepo = new FakeReadingStateRepository();
        var service = CreateService([book], readingStateRepo, proxyService);
        var request = MakeRequest(MakeAnalyticsRequest(MakeUnknownEventWithVolumeId(book.EntitlementUuid)));

        await service.AnalyticsEvent(request);

        Assert.IsFalse(proxyService.WasCalled, "Local unknown event should not be proxied");
        Assert.IsNull(readingStateRepo.GetStored(book.BookUuid), "Unknown event should not create reading state");
    }

    [TestMethod]
    public async Task EventWithoutVolumeId_Proxied()
    {
        var proxyService = new FakeProxyService();
        var service = CreateService([], proxyService: proxyService);
        var request = MakeRequest(MakeAnalyticsRequest(MakeSyncEvent()));

        await service.AnalyticsEvent(request);

        Assert.IsTrue(proxyService.WasCalled);
        var proxiedBody = JsonSerializer.Deserialize<KoboStoreAnalyticsRequest>(proxyService.LastRequest!.Body);
        Assert.HasCount(1, proxiedBody!.Events!);
        Assert.AreEqual("StatusBarOption", proxiedBody.Events![0].EventType);
    }

    // --- Test doubles ---

    private class FakeLibraryService(List<Book> books) : IAlexandriaLibraryService
    {
        public Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null) => Task.FromResult(books);
        public Task<Book?> GetBookByUuid(Guid uuid) => Task.FromResult(books.FirstOrDefault(b => b.BookUuid == uuid));
        public Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid) => Task.FromResult(books.FirstOrDefault(b => b.EntitlementUuid == entitlementUuid));
        public Task SetBookArchiveStatus(Guid uuid, bool isArchived) => Task.CompletedTask;
        public Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null) => Task.FromResult<List<BookCollection>>([]);
        public Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null) => Task.FromResult<CollectionWithBooks?>(null);
        public Task UpsertCollection(BookCollection collection) => Task.CompletedTask;
        public Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since) => Task.FromResult<List<BookCollection>>([]);
        public Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null) => Task.FromResult<List<Author>>([]);
        public Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null) => Task.FromResult<Author?>(null);
        public Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null) => Task.FromResult<List<Series>>([]);
        public Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null) => Task.FromResult<Series?>(null);
        public Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null) => Task.FromResult<byte[]?>(null);
        public Task UpdateReadingStatus(Guid uuid, ReadingStatus status) => Task.CompletedTask;
        public Task<Book?> UpdateBookMetadata(Book book) => Task.FromResult<Book?>(null);
        public Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType) => Task.FromResult<Book?>(null);
        public Task<Book?> RemoveBookCover(Guid uuid) => Task.FromResult<Book?>(null);
        public Task<int> Sync(IProgress<(int current, int total)>? progress = null) => Task.FromResult(0);
        public Task<Author?> GetAuthorByUuid(Guid uuid, BookSortOptions? sortOptions = null) => Task.FromResult<Author?>(null);
        public Task<Author?> UpdateAuthor(Author author) => Task.FromResult<Author?>(null);
        public Task<Series?> UpdateSeries(Series series) => Task.FromResult<Series?>(null);
        public Task<Series?> GetSeriesByName(string name) => Task.FromResult<Series?>(null);
        public Task<List<string>> GetPublishers() => Task.FromResult<List<string>>([]);
        public Task<List<Book>> GetRecommendations(Guid bookUuid, int count = 5) => Task.FromResult<List<Book>>([]);
        public Task<Author?> MergeAuthor(Guid sourceUuid, Guid targetUuid) => Task.FromResult<Author?>(null);
        public Task<Series?> MergeSeries(Guid sourceUuid, Guid targetUuid) => Task.FromResult<Series?>(null);
    }

    internal class FakeReadingStateRepository : IReadingStateRepository
    {
        private readonly Dictionary<Guid, KoboStoreReadingState> store = new();

        public void Seed(Guid bookUuid, KoboStoreReadingState state) => store[bookUuid] = state;

        public KoboStoreReadingState? GetStored(Guid bookUuid) => store.TryGetValue(bookUuid, out var state) ? state : null;

        public Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid) => Task.FromResult(store.TryGetValue(bookUuid, out var state) ? state : null);

        public Task<ReadingProgress?> GetReadingProgress(Guid bookUuid) => Task.FromResult<ReadingProgress?>(null);

        public Task Upsert(Guid bookUuid, KoboStoreReadingState state)
        {
            store[bookUuid] = state;
            return Task.CompletedTask;
        }
    }

    internal class FakeProxyService : IKoboProxyService
    {
        public bool WasCalled { get; private set; }
        public KoboProxyRequest? LastRequest { get; private set; }

        public Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
        {
            WasCalled = true;
            LastRequest = request;
            return Task.FromResult(new KoboProxyResponse { StatusCode = 200, Headers = new Dictionary<string, string[]>(), Body = Encoding.UTF8.GetBytes("{}") });
        }
    }
}
