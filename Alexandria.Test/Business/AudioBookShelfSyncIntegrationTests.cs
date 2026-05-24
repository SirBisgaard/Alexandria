using System.Net;
using System.Text;
using System.Text.Json;
using Alexandria.Business.Integrations.Models;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class AudioBookShelfSyncIntegrationTests
{
    private const string ServerUrl = "https://abs.example.com";
    private const string ApiToken = "test-token";

    private FakeAbsHttpHandler handler = null!;
    private HttpClient httpClient = null!;
    private FakeAudioBookShelfSyncIntegrationRepository absRepository = null!;
    private FakeBookRepository bookRepository = null!;
    private FakeReadingStateRepository readingStateRepository = null!;
    private FakeBookActivityEventRepository activityEventRepository = null!;
    private AudioBookShelfSyncIntegration sut = null!;

    [TestInitialize]
    public void Setup()
    {
        handler = new FakeAbsHttpHandler();
        httpClient = new HttpClient(handler);
        absRepository = new FakeAudioBookShelfSyncIntegrationRepository();
        bookRepository = new FakeBookRepository();
        readingStateRepository = new FakeReadingStateRepository();
        activityEventRepository = new FakeBookActivityEventRepository();
        sut = new AudioBookShelfSyncIntegration(httpClient, absRepository, bookRepository, readingStateRepository, activityEventRepository, NullLogger<AudioBookShelfSyncIntegration>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => httpClient.Dispose();

    [TestMethod]
    public async Task VerifyConnection_Success_ReturnsTrue()
    {
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, "{}");

        var ok = await sut.VerifyConnection(ServerUrl, ApiToken);

        Assert.IsTrue(ok);
    }

    [TestMethod]
    public async Task VerifyConnection_Non2xxResponse_ReturnsFalse()
    {
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.Unauthorized, "{}");

        var ok = await sut.VerifyConnection(ServerUrl, ApiToken);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public async Task VerifyConnection_HandlerThrows_ReturnsFalse()
    {
        handler.RegisterException(HttpMethod.Get, "/api/me", new HttpRequestException("boom"));

        var ok = await sut.VerifyConnection(ServerUrl, ApiToken);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public async Task Sync_NoEnabledIntegrations_DoesNothing()
    {
        await sut.Sync(CancellationToken.None);

        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Sync_MatchedLocalNewer_PushesAndStampsLastSynced()
    {
        var integration = SeedEnabledIntegration();
        var book = SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 50m, modifiedAt: DateTimeOffset.UtcNow);
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        // No mediaProgress for the item → abs side is "no record"; local has progress → push branch.
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, """{"mediaProgress":[]}""");
        RegisterSessionEndpoints("abs-1", "session-1");

        await sut.Sync(CancellationToken.None);

        Assert.IsTrue(handler.HasRequest(HttpMethod.Post, "/api/items/abs-1/play"));
        Assert.IsTrue(handler.HasRequest(HttpMethod.Post, "/api/session/session-1/sync"));
        Assert.IsTrue(handler.HasRequest(HttpMethod.Post, "/api/session/session-1/close"));
        Assert.IsNotNull(absRepository.Find(integration.UserUuid)!.LastSyncedAt);
        Assert.IsTrue(absRepository.HasBookSyncedState(integration.UserUuid, book.BookUuid));
    }

    [TestMethod]
    public async Task Sync_MatchedAbsNewer_PullsAndOverwritesLocalReadingState()
    {
        var integration = SeedEnabledIntegration();
        var book = SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 10m, modifiedAt: DateTimeOffset.UtcNow.AddHours(-1));
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        // ABS lastUpdate well after local → pull. progress 0.75, currentTime 75s, not finished.
        var absLastUpdateMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK,
            $$"""{"mediaProgress":[{"libraryItemId":"abs-1","lastUpdate":{{absLastUpdateMs}},"progress":0.75,"isFinished":false,"currentTime":75,"duration":100}]}""");

        await sut.Sync(CancellationToken.None);

        var stored = readingStateRepository.FindState(book.BookUuid);
        Assert.IsNotNull(stored);
        Assert.AreEqual("Reading", stored.StatusInfo.Status);
        Assert.AreEqual(75m, Convert.ToDecimal(stored.CurrentBookmark.ProgressPercent));
        Assert.IsTrue(absRepository.HasBookSyncedState(integration.UserUuid, book.BookUuid));
        // Pull path does NOT call any session endpoint.
        Assert.IsFalse(handler.HasRequest(HttpMethod.Post, "/api/items/abs-1/play"));
    }

    [TestMethod]
    public async Task Sync_AbsHasNoProgress_NoPriorSyncedState_PushesLocalUp()
    {
        var integration = SeedEnabledIntegration();
        var book = SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 30m, modifiedAt: DateTimeOffset.UtcNow);
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, """{"mediaProgress":[]}""");
        RegisterSessionEndpoints("abs-1", "session-1");

        await sut.Sync(CancellationToken.None);

        Assert.IsTrue(handler.HasRequest(HttpMethod.Post, "/api/items/abs-1/play"), "first-sighting push should fire");
        Assert.IsTrue(absRepository.HasBookSyncedState(integration.UserUuid, book.BookUuid));
        // Local progress is unchanged — we only push, not pull.
        Assert.IsNull(readingStateRepository.FindState(book.BookUuid));
    }

    [TestMethod]
    public async Task Sync_AbsHasNoProgress_PriorSyncedState_ClearsLocalAndRemovesTombstone()
    {
        var integration = SeedEnabledIntegration();
        var book = SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 30m, modifiedAt: DateTimeOffset.UtcNow);
        // Tombstone candidate: book was once on ABS, we recorded a synced state, now ABS has nothing.
        absRepository.SeedBookSyncedState(integration.UserUuid, book.BookUuid, DateTimeOffset.UtcNow.AddHours(-1));
        readingStateRepository.SeedState(book.BookUuid, BuildReadingState());
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, """{"mediaProgress":[]}""");

        await sut.Sync(CancellationToken.None);

        Assert.IsNull(readingStateRepository.FindState(book.BookUuid), "local progress should be cleared when ABS dropped its entry");
        Assert.IsFalse(absRepository.HasBookSyncedState(integration.UserUuid, book.BookUuid), "tombstone row should be cleared");
        // No push attempted.
        Assert.IsFalse(handler.HasRequest(HttpMethod.Post, "/api/items/abs-1/play"));
    }

    [TestMethod]
    public async Task Sync_LastSyncedAt_StampedAfterReconcileLoop()
    {
        var integration = SeedEnabledIntegration();
        SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 50m, modifiedAt: DateTimeOffset.UtcNow);
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, """{"mediaProgress":[]}""");
        RegisterSessionEndpoints("abs-1", "session-1");
        var before = DateTimeOffset.UtcNow;

        await sut.Sync(CancellationToken.None);

        var after = absRepository.Find(integration.UserUuid)!.LastSyncedAt;
        Assert.IsNotNull(after);
        Assert.IsGreaterThanOrEqualTo(before.ToUnixTimeSeconds(), after.Value.ToUnixTimeSeconds());
    }

    [TestMethod]
    public async Task SyncForUser_DisabledIntegration_NoNetworkCalls()
    {
        var integration = SeedDisabledIntegration();

        await sut.SyncForUser(integration.UserUuid, CancellationToken.None);

        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task SyncForUser_IgnoresCutoff_ProcessesMatchesOlderThanLastSynced()
    {
        var integration = SeedEnabledIntegration();
        // LastSyncedAt is in the future relative to the book's match timestamps → background
        // Sync would filter the match out via the cutoff. SyncForUser should still process it.
        integration.LastSyncedAt = DateTimeOffset.UtcNow.AddDays(1);
        absRepository.Seed(integration);
        var book = SeedAudiobook("Fellowship.m4b", duration: 100, progressPercent: 50m, modifiedAt: DateTimeOffset.UtcNow);
        RegisterAbsLibraryWithItems((itemId: "abs-1", relPath: "Fellowship.m4b"));
        handler.RegisterJson(HttpMethod.Get, "/api/me", HttpStatusCode.OK, """{"mediaProgress":[]}""");
        RegisterSessionEndpoints("abs-1", "session-1");

        await sut.SyncForUser(integration.UserUuid, CancellationToken.None);

        Assert.IsTrue(handler.HasRequest(HttpMethod.Post, "/api/items/abs-1/play"), "manual sync should ignore the cutoff and push anyway");
    }

    // ----- helpers -----

    private AudioBookShelfIntegration SeedEnabledIntegration()
    {
        var integration = new AudioBookShelfIntegration
        {
            UserUuid = Guid.NewGuid(),
            Enabled = true,
            ServerUrl = ServerUrl,
            ApiToken = ApiToken
        };
        absRepository.Seed(integration);
        return integration;
    }

    private AudioBookShelfIntegration SeedDisabledIntegration()
    {
        var integration = new AudioBookShelfIntegration
        {
            UserUuid = Guid.NewGuid(),
            Enabled = false,
            ServerUrl = ServerUrl,
            ApiToken = ApiToken
        };
        absRepository.Seed(integration);
        return integration;
    }

    private Book SeedAudiobook(string fileName, int duration, decimal progressPercent, DateTimeOffset modifiedAt)
    {
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = fileName,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ModifiedAt = modifiedAt,
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Audiobook,
                SourcePath = $"/library/{fileName}",
                Duration = duration
            },
            Authors = [],
            Chapters = [],
            Contributors = []
        };
        bookRepository.Seed(book);
        readingStateRepository.SeedProgress(uuid, new ReadingProgress
        {
            Status = ReadingStatus.Reading,
            ProgressPercent = progressPercent,
            ModifiedAt = modifiedAt,
            CreatedAt = book.CreatedAt
        });
        return book;
    }

    private void RegisterAbsLibraryWithItems(params (string itemId, string relPath)[] items)
    {
        // Items endpoint must be registered BEFORE the bare `/api/libraries` route so prefix-
        // matching reaches the more specific route first; otherwise the items request lands on
        // the libraries-list responder.
        var itemsJson = string.Join(",", items.Select(i =>
            "{\"id\":\"" + i.itemId + "\",\"path\":\"/audiobooks/" + i.relPath + "\",\"relPath\":\"" + i.relPath + "\",\"media\":{\"audioFiles\":[]}}"));
        handler.RegisterJson(HttpMethod.Get, "/api/libraries/lib-1/items", HttpStatusCode.OK,
            "{\"results\":[" + itemsJson + "],\"total\":" + items.Length + "}");
        handler.RegisterJson(HttpMethod.Get, "/api/libraries", HttpStatusCode.OK,
            """{"libraries":[{"id":"lib-1","name":"Audiobooks"}]}""");
    }

    private void RegisterSessionEndpoints(string itemId, string sessionId)
    {
        handler.RegisterJson(HttpMethod.Post, $"/api/items/{itemId}/play", HttpStatusCode.OK,
            "{\"id\":\"" + sessionId + "\"}");
        handler.RegisterText(HttpMethod.Post, $"/api/session/{sessionId}/sync", HttpStatusCode.OK, "OK");
        handler.RegisterText(HttpMethod.Post, $"/api/session/{sessionId}/close", HttpStatusCode.OK, "OK");
    }

    private static KoboStoreReadingState BuildReadingState()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new KoboStoreReadingState
        {
            EntitlementId = Guid.NewGuid().ToString(),
            Created = now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = now, Status = "Reading", TimesStartedReading = 1, LastTimeStartedReading = now },
            Statistics = new KoboStoreStatistics { LastModified = now },
            CurrentBookmark = new KoboStoreBookmark { LastModified = now, ProgressPercent = 30 }
        };
    }

    // ----- fakes -----

    private sealed class FakeAbsHttpHandler : HttpMessageHandler
    {
        private readonly List<Registration> registrations = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public void RegisterJson(HttpMethod method, string pathPrefix, HttpStatusCode statusCode, string jsonBody)
        {
            registrations.Add(new Registration(method, pathPrefix, _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            }));
        }

        public void RegisterText(HttpMethod method, string pathPrefix, HttpStatusCode statusCode, string body)
        {
            registrations.Add(new Registration(method, pathPrefix, _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain")
            }));
        }

        public void RegisterException(HttpMethod method, string pathPrefix, Exception exception)
        {
            registrations.Add(new Registration(method, pathPrefix, _ => throw exception));
        }

        public bool HasRequest(HttpMethod method, string pathPrefix) =>
            Requests.Any(r => r.Method == method && r.RequestUri!.PathAndQuery.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.PathAndQuery;
            foreach (var reg in registrations)
            {
                if (reg.Method != request.Method) continue;
                if (!path.StartsWith(reg.PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                return Task.FromResult(reg.Responder(request));
            }
            throw new InvalidOperationException($"FakeAbsHttpHandler: no registration for {request.Method} {path}");
        }

        private sealed record Registration(HttpMethod Method, string PathPrefix, Func<HttpRequestMessage, HttpResponseMessage> Responder);
    }

    private sealed class FakeAudioBookShelfSyncIntegrationRepository : IAudioBookShelfSyncIntegrationRepository
    {
        private readonly Dictionary<Guid, AudioBookShelfIntegration> integrations = [];
        private readonly Dictionary<(Guid, Guid), DateTimeOffset> bookStates = [];

        public void Seed(AudioBookShelfIntegration integration) => integrations[integration.UserUuid] = integration;

        public void SeedBookSyncedState(Guid userUuid, Guid bookUuid, DateTimeOffset timestamp) =>
            bookStates[(userUuid, bookUuid)] = timestamp;

        public AudioBookShelfIntegration? Find(Guid userUuid) => integrations.GetValueOrDefault(userUuid);

        public bool HasBookSyncedState(Guid userUuid, Guid bookUuid) => bookStates.ContainsKey((userUuid, bookUuid));

        public Task Upsert(AudioBookShelfIntegration integration)
        {
            integrations[integration.UserUuid] = integration;
            return Task.CompletedTask;
        }

        public Task<AudioBookShelfIntegration?> GetByUserUuid(Guid userUuid) =>
            Task.FromResult(integrations.GetValueOrDefault(userUuid));

        public Task<List<AudioBookShelfIntegration>> GetAllEnabled() =>
            Task.FromResult(integrations.Values.Where(i => i.Enabled).ToList());

        public Task UpdateLastSynced(Guid userUuid, DateTimeOffset timestamp)
        {
            if (integrations.TryGetValue(userUuid, out var i))
                i.LastSyncedAt = timestamp;
            return Task.CompletedTask;
        }

        public Task<Dictionary<Guid, DateTimeOffset>> GetBookSyncedStates(Guid userUuid)
        {
            var result = bookStates
                .Where(kv => kv.Key.Item1 == userUuid)
                .ToDictionary(kv => kv.Key.Item2, kv => kv.Value);
            return Task.FromResult(result);
        }

        public Task UpsertBookSyncedState(Guid userUuid, Guid bookUuid, DateTimeOffset absLastUpdate)
        {
            bookStates[(userUuid, bookUuid)] = absLastUpdate;
            return Task.CompletedTask;
        }

        public Task DeleteBookSyncedState(Guid userUuid, Guid bookUuid)
        {
            bookStates.Remove((userUuid, bookUuid));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBookRepository : IBookRepository
    {
        private readonly Dictionary<Guid, Book> byUuid = [];

        public void Seed(Book book) => byUuid[book.BookUuid] = book;

        public Task<List<Book>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<List<BookCoverKey>> GetCoverLookupKeys() => Task.FromResult(new List<BookCoverKey>());
        public Task<Book?> FindByUuid(Guid uuid) => Task.FromResult(byUuid.GetValueOrDefault(uuid));
        public Task<Book?> FindByEntitlementUuid(Guid e) => Task.FromResult<Book?>(null);
        public Task<Book?> FindByPath(string sourcePath) => Task.FromResult<Book?>(null);
        public Task Add(Book book) { byUuid[book.BookUuid] = book; return Task.CompletedTask; }
        public Task Update(Book book) { byUuid[book.BookUuid] = book; return Task.CompletedTask; }
        public Task Remove(Guid uuid) { byUuid.Remove(uuid); return Task.CompletedTask; }
        public Task<List<string>> GetPublishers() => Task.FromResult(new List<string>());
    }

    private sealed class FakeReadingStateRepository : IReadingStateRepository
    {
        private readonly Dictionary<Guid, KoboStoreReadingState> states = [];
        private readonly Dictionary<Guid, ReadingProgress> progress = [];

        public void SeedState(Guid bookUuid, KoboStoreReadingState state) => states[bookUuid] = state;
        public void SeedProgress(Guid bookUuid, ReadingProgress p) => progress[bookUuid] = p;
        public KoboStoreReadingState? FindState(Guid bookUuid) => states.GetValueOrDefault(bookUuid);

        public Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid) =>
            Task.FromResult(states.GetValueOrDefault(bookUuid));
        public Task<ReadingProgress?> GetReadingProgress(Guid bookUuid) =>
            Task.FromResult(progress.GetValueOrDefault(bookUuid));
        public Task<bool> Upsert(Guid bookUuid, KoboStoreReadingState state) { states[bookUuid] = state; return Task.FromResult(true); }
        public Task DeleteByBookUuid(Guid bookUuid) { states.Remove(bookUuid); progress.Remove(bookUuid); return Task.CompletedTask; }
    }

    private sealed class FakeBookActivityEventRepository : IBookActivityEventRepository
    {
        private readonly List<BookActivityEvent> events = [];

        public IReadOnlyList<BookActivityEvent> Events => events;

        public Task Insert(BookActivityEvent activityEvent)
        {
            events.Add(activityEvent);
            return Task.CompletedTask;
        }

        public Task<List<BookActivityEvent>> GetByBookUuid(Guid bookUuid) =>
            Task.FromResult(events.Where(e => e.BookUuid == bookUuid).OrderByDescending(e => e.EventTimestamp).ToList());
    }
}
