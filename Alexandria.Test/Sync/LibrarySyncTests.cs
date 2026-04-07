using System.Net;
using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;
using Alexandria.Business.Models.Proxy;
using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Sync;

[TestClass]
public sealed class LibrarySyncTests
{
    private const string DeviceId = "test-device-id";
    private const string KoboStoreToken = "raw-kobo-store-token";
    private const string StoreApi = "https://storeapi.kobo.com";
    private const string AlexandriaApi = "http://localhost";

    private readonly SyncTokenService syncTokenService = new();
    private readonly SyncSessionService syncSessionService = new();

    private static readonly KoboProxySettings Settings = new() { AlexandriaApi = AlexandriaApi, StoreApi = StoreApi };

    private static Book MakeBook(string title, DateTimeOffset createdAt, DateTimeOffset? lastModified = null)
    {
        var uuid = Guid.NewGuid();
        return new Book { BookUuid = uuid, EntitlementUuid = uuid, Title = title, Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Test Author" }], Language = "en", FileHash = "sha256:abc", SourcePath = "/test.epub", KepubFileSize = 1000, CreatedAt = createdAt, LastModified = lastModified ?? createdAt };
    }

    private static KoboProxyRequest MakeRequest(string? syncToken = null)
    {
        var headers = new Dictionary<string, string?[]> { ["x-kobo-deviceid"] = [DeviceId] };
        if (syncToken is not null)
        {
            headers["x-kobo-synctoken"] = [syncToken];
        }

        return new KoboProxyRequest { Method = "GET", Path = "/v1/library/sync", QueryString = "", Headers = headers, Body = [] };
    }

    private KoboStoreProxyService CreateService(List<Book> books, HttpResponseMessage? upstreamResponse = null)
    {
        var libraryService = new FakeLibraryService(books);
        var fileRepository = new FakeFileRepository();
        var readingStateRepository = new FakeReadingStateRepository();
        var handler = new FakeHttpMessageHandler(upstreamResponse ?? MakeEmptyStoreResponse());
        var httpClient = new HttpClient(handler);
        return new KoboStoreProxyService(Settings, httpClient, libraryService, new FakeSettingsService(), fileRepository, readingStateRepository, syncSessionService, syncTokenService, NullLogger<KoboStoreProxyService>.Instance);
    }

    private static HttpResponseMessage MakeEmptyStoreResponse(string? syncToken = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        if (syncToken is not null)
        {
            response.Headers.TryAddWithoutValidation("x-kobo-synctoken", syncToken);
        }

        return response;
    }

    private static HttpResponseMessage MakeContinuationStoreResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("x-kobo-sync", "continue");
        response.Headers.TryAddWithoutValidation("x-kobo-synctoken", KoboStoreToken);
        return response;
    }

    private static List<SyncResultItem> DeserializeBody(KoboProxyResponse response)
    {
        return JsonSerializer.Deserialize<List<SyncResultItem>>(response.Body) ?? [];
    }

    private KoboDeviceSyncToken ParseResponseToken(KoboProxyResponse response)
    {
        var tokenHeader = response.Headers.TryGetValue("x-kobo-synctoken", out var values) && values.Length > 0 ? values[0] : null;
        Assert.IsNotNull(tokenHeader, "Response should contain x-kobo-synctoken header");
        return syncTokenService.Parse(tokenHeader);
    }

    [TestMethod]
    public async Task FirstSync_RawKoboToken_SendsAllBooksAsNewEntitlement()
    {
        var now = DateTimeOffset.UtcNow;
        var books = new List<Book> { MakeBook("Book A", now.AddMinutes(-10)), MakeBook("Book B", now.AddMinutes(-5)) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));

        var response = await service.LibrarySync(MakeRequest(KoboStoreToken));

        var items = DeserializeBody(response);
        Assert.HasCount(2, items);
        Assert.IsTrue(items.All(i => i.NewEntitlement is not null), "All items should be NewEntitlement on first sync");
        Assert.IsTrue(items.All(i => i.ChangedEntitlement is null));
    }

    [TestMethod]
    public async Task SubsequentSync_NothingChanged_ReturnsZeroLocalBooks()
    {
        var lastSync = DateTimeOffset.UtcNow;
        var oldTime = lastSync.AddHours(-1);
        var books = new List<Book> { MakeBook("Book A", oldTime), MakeBook("Book B", oldTime) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var items = DeserializeBody(response);
        Assert.IsEmpty(items, "No local books should be sent when nothing changed");
    }

    [TestMethod]
    public async Task SubsequentSync_NewBookAdded_SendsOnlyNewBookAsNewEntitlement()
    {
        var lastSync = DateTimeOffset.UtcNow.AddMinutes(-5);
        var oldBook = MakeBook("Old Book", lastSync.AddHours(-1));
        var newBook = MakeBook("New Book", DateTimeOffset.UtcNow);
        var books = new List<Book> { oldBook, newBook };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var items = DeserializeBody(response);
        Assert.HasCount(1, items, "Only the new book should be sent");
        Assert.IsNotNull(items[0].NewEntitlement, "New book should be sent as NewEntitlement");
        Assert.AreEqual("New Book", items[0].NewEntitlement!.BookMetadata!.Title);
    }

    [TestMethod]
    public async Task SubsequentSync_BookUpdated_SendsRemovalAndNewEntitlement()
    {
        var lastSync = DateTimeOffset.UtcNow.AddMinutes(-5);
        var unchangedBook = MakeBook("Unchanged", lastSync.AddHours(-1));
        var updatedBook = MakeBook("Updated", lastSync.AddHours(-1), lastModified: DateTimeOffset.UtcNow);
        var books = new List<Book> { unchangedBook, updatedBook };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var items = DeserializeBody(response);
        Assert.HasCount(2, items, "Updated book should produce a removal + re-add");
        Assert.IsNotNull(items[0].ChangedEntitlement, "First item should be a ChangedEntitlement removal");
        Assert.IsTrue(items[0].ChangedEntitlement!.BookEntitlement!.IsRemoved);
        Assert.IsNotNull(items[1].NewEntitlement, "Second item should be a NewEntitlement re-add");
        Assert.AreEqual("Updated", items[1].NewEntitlement!.BookMetadata!.Title);
    }

    [TestMethod]
    public async Task SubsequentSync_ArchivedBookIgnored()
    {
        var lastSync = DateTimeOffset.UtcNow.AddMinutes(-5);
        var archivedBook = MakeBook("Archived", DateTimeOffset.UtcNow);
        archivedBook.IsArchived = true;
        var books = new List<Book> { archivedBook };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var items = DeserializeBody(response);
        Assert.IsEmpty(items, "Archived books should not be sent");
    }

    [TestMethod]
    public async Task FirstSync_ResponseTokenWrapsKoboToken()
    {
        var books = new List<Book> { MakeBook("Book A", DateTimeOffset.UtcNow) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));

        var response = await service.LibrarySync(MakeRequest(KoboStoreToken));

        var parsedToken = ParseResponseToken(response);
        Assert.AreEqual(KoboStoreToken, parsedToken.KoboToken, "Wrapped token should preserve the Kobo store token");
    }

    [TestMethod]
    public async Task CompletedSync_AdvancesLastSync()
    {
        var before = DateTimeOffset.UtcNow;
        var books = new List<Book> { MakeBook("Book A", DateTimeOffset.UtcNow) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));

        var response = await service.LibrarySync(MakeRequest(KoboStoreToken));

        var parsedToken = ParseResponseToken(response);
        Assert.IsTrue(parsedToken.LastSync >= before, "LastSync should be advanced to approximately now after a completed sync");
    }

    [TestMethod]
    public async Task NoChanges_AdvancesLastSync()
    {
        var oldLastSync = DateTimeOffset.UtcNow.AddHours(-1);
        var books = new List<Book> { MakeBook("Old Book", oldLastSync.AddHours(-1)) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = oldLastSync });
        var before = DateTimeOffset.UtcNow;

        var response = await service.LibrarySync(MakeRequest(token));

        var parsedToken = ParseResponseToken(response);
        Assert.IsTrue(parsedToken.LastSync >= before, "LastSync should be advanced even when there are no changes");
    }

    [TestMethod]
    public async Task Continuation_LastSyncNotAdvancedUntilFinalPage()
    {
        var now = DateTimeOffset.UtcNow;
        var books = Enumerable.Range(0, 150).Select(i => MakeBook($"Book {i}", now)).ToList();
        var libraryService = new FakeLibraryService(books);
        var handler = new FakeHttpMessageHandler(MakeEmptyStoreResponse(KoboStoreToken));
        var httpClient = new HttpClient(handler);
        var service = new KoboStoreProxyService(Settings, httpClient, libraryService, new FakeSettingsService(100), new FakeFileRepository(), new FakeReadingStateRepository(), syncSessionService, syncTokenService, NullLogger<KoboStoreProxyService>.Instance);

        // First page — should have continue header and lastSync NOT advanced.
        var response1 = await service.LibrarySync(MakeRequest(KoboStoreToken));
        var items1 = DeserializeBody(response1);
        Assert.HasCount(100, items1, "First page should have 100 items");
        Assert.IsTrue(response1.Headers.TryGetValue("x-kobo-sync", out var continueValues) && continueValues.Contains("continue"), "First page should have x-kobo-sync: continue");
        var token1 = ParseResponseToken(response1);
        Assert.AreEqual(DateTimeOffset.MinValue, token1.LastSync, "LastSync should NOT be advanced during continuation");

        // Second page (final) — should advance lastSync.
        var before = DateTimeOffset.UtcNow;
        var response2 = await service.LibrarySync(MakeRequest(syncTokenService.Generate(token1)));
        var items2 = DeserializeBody(response2);
        Assert.HasCount(50, items2, "Second page should have the remaining 50 items");
        Assert.IsFalse(response2.Headers.ContainsKey("x-kobo-sync"), "Final page should NOT have x-kobo-sync header");
        var token2 = ParseResponseToken(response2);
        Assert.IsTrue(token2.LastSync >= before, "LastSync should be advanced on the final page");
    }

    [TestMethod]
    public async Task KoboContinuation_PassthroughWithWrappedToken()
    {
        var lastSync = DateTimeOffset.UtcNow.AddHours(-1);
        var books = new List<Book> { MakeBook("Book A", DateTimeOffset.UtcNow) };
        var libraryService = new FakeLibraryService(books);
        var handler = new FakeHttpMessageHandler(MakeContinuationStoreResponse());
        var httpClient = new HttpClient(handler);
        var service = new KoboStoreProxyService(Settings, httpClient, libraryService, new FakeSettingsService(), new FakeFileRepository(), new FakeReadingStateRepository(), syncSessionService, syncTokenService, NullLogger<KoboStoreProxyService>.Instance);
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var parsedToken = ParseResponseToken(response);
        Assert.AreEqual(KoboStoreToken, parsedToken.KoboToken, "Kobo token should be preserved during Kobo continuation");
        Assert.AreEqual(lastSync, parsedToken.LastSync, "LastSync should NOT be advanced during Kobo continuation");
    }

    [TestMethod]
    public async Task FirstSync_NoSyncTokenHeader_TreatedAsFullSync()
    {
        var books = new List<Book> { MakeBook("Book A", DateTimeOffset.UtcNow) };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));

        var response = await service.LibrarySync(MakeRequest(syncToken: null));

        var items = DeserializeBody(response);
        Assert.HasCount(1, items, "Missing token should trigger full sync");
        Assert.IsNotNull(items[0].NewEntitlement);
    }

    [TestMethod]
    public async Task MixedNewAndChanged_CorrectEntitlementTypes()
    {
        var lastSync = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newBook = MakeBook("New Book", DateTimeOffset.UtcNow);
        var changedBook = MakeBook("Changed Book", lastSync.AddHours(-1), lastModified: DateTimeOffset.UtcNow);
        var oldBook = MakeBook("Old Book", lastSync.AddHours(-1));
        var books = new List<Book> { newBook, changedBook, oldBook };
        var service = CreateService(books, MakeEmptyStoreResponse(KoboStoreToken));
        var token = syncTokenService.Generate(new KoboDeviceSyncToken { KoboToken = KoboStoreToken, LastSync = lastSync });

        var response = await service.LibrarySync(MakeRequest(token));

        var items = DeserializeBody(response);
        Assert.HasCount(3, items, "New book (1 item) + changed book (removal + re-add = 2 items)");
        var newItems = items.Where(i => i.NewEntitlement is not null).ToList();
        var changedItems = items.Where(i => i.ChangedEntitlement is not null).ToList();
        Assert.HasCount(2, newItems, "New book + changed book re-add");
        Assert.HasCount(1, changedItems, "Changed book removal");
        Assert.IsTrue(changedItems[0].ChangedEntitlement!.BookEntitlement!.IsRemoved);
    }

    // --- Test doubles ---

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private class FakeLibraryService(List<Book> books) : IAlexandriaLibraryService
    {
        public Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null) => Task.FromResult(books);
        public Task<Book?> GetBookByUuid(Guid uuid) => Task.FromResult(books.FirstOrDefault(b => b.BookUuid == uuid));
        public Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid) => Task.FromResult(books.FirstOrDefault(b => b.EntitlementUuid == entitlementUuid));
        public Task<Book?> GetBookByCoverImageUuid(Guid uuid) => Task.FromResult(books.FirstOrDefault(b => b.CoverImageUuid == uuid));
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
    }

    private class FakeFileRepository : IFileRepository
    {
        public Task<byte[]?> ReadKepub(Book book) => Task.FromResult<byte[]?>(null);
        public Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null) => Task.FromResult<byte[]?>(null);
        public void DeleteCover(Book book) { }
        public void DeleteKepub(Book book) { }
        public string[] GetEpubFiles() => [];
        public Task<string> ComputeFileHash(string filePath) => Task.FromResult("sha256:fake");
        public long GetKepubFileSize(string kepubPath) => 0;
    }

    private class FakeReadingStateRepository : IReadingStateRepository
    {
        public Task<ReadingState?> GetByBookUuid(Guid bookUuid) => Task.FromResult<ReadingState?>(null);
        public Task<ReadingProgress?> GetReadingProgress(Guid bookUuid) => Task.FromResult<ReadingProgress?>(null);
        public Task Upsert(Guid bookUuid, ReadingState state, string rawPutBody) => Task.CompletedTask;
    }

    private class FakeSettingsService(int syncBatchSize = 60) : IAlexandriaSettingsService
    {
        public Task<AppSettings> Get() => Task.FromResult(new AppSettings { SyncBatchSize = syncBatchSize });
        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp) => Task.CompletedTask;
        public Task UpdateSettings(AppSettings settings) => Task.CompletedTask;
    }

}
