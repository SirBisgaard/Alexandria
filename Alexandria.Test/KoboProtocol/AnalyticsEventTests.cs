using System.Net;
using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/analytics/event receives a bag of device telemetry events. Alexandria handles the
/// user-driven state transitions — <c>MarkAsFinished</c> and <c>MarkAsUnread</c> — for local books.
/// All other local event types (BookOpened, BookProgress, Playback, StartReadingBook, etc.) are
/// observational telemetry and are dropped; progress is authoritatively carried by
/// <c>PUT /v1/library/{uuid}/state</c> instead. Events for books Alexandria doesn't own, and events
/// with no volume id at all, are forwarded to Kobo so the store can keep its own analytics. A
/// mixed batch triggers a filtered-forward: the proxied request body contains only the non-local
/// events, with <c>Content-Length</c> stripped so the HttpClient recomputes it.
/// </summary>
[TestClass]
public sealed class AnalyticsEventTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task AnalyticsEvent_MarkAsFinishedForLocalBook_UpdatesReadingStateToFinished()
    {
        var fixture = FixtureLoader.LoadAll("analytics-event").First();
        var entitlementUuid = ExtractMarkAsFinishedVolumeId(fixture.Request.Body);
        await SeedRecordedDevice();
        var book = await SeedLocalBook(entitlementUuid);
        // The fixture batch also contains non-local events — register an upstream responder so
        // Alexandria can forward the filtered remainder without hitting real Kobo.
        Upstream.RegisterJson(HttpMethod.Post, "/v1/analytics/event", HttpStatusCode.OK, """{"Result":"Success","AcceptedEvents":[],"RejectedEvents":[]}""");

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var readingStateRepo = GetService<IReadingStateRepository>();
        var stored = await readingStateRepo.GetByBookUuid(book.BookUuid);
        Assert.IsNotNull(stored, "MarkAsFinished must have upserted a reading state");
        Assert.AreEqual("Finished", stored.StatusInfo.Status);
    }

    [TestMethod]
    public async Task AnalyticsEvent_MixedBatch_ForwardsFilteredEventsToKobo()
    {
        var fixture = FixtureLoader.LoadAll("analytics-event").First();
        var entitlementUuid = ExtractMarkAsFinishedVolumeId(fixture.Request.Body);
        await SeedRecordedDevice();
        await SeedLocalBook(entitlementUuid);
        Upstream.RegisterJson(HttpMethod.Post, "/v1/analytics/event", HttpStatusCode.OK, """{"Result":"Success","AcceptedEvents":[],"RejectedEvents":[]}""");

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        Assert.AreEqual(1, Upstream.Requests.Count, "mixed batch must forward the non-local events to Kobo");
        var forwardedBody = await Upstream.Requests[0].Content!.ReadAsStringAsync();
        // The forwarded body must not contain MarkAsFinished (that was handled locally).
        Assert.IsFalse(forwardedBody.Contains("MarkAsFinished"), "MarkAsFinished was handled locally and must be filtered out of the upstream request");
    }

    [TestMethod]
    public async Task AnalyticsEvent_EmptyEvents_ProxiesToKobo()
    {
        // Synthetic test: an empty-events batch must round-trip to Kobo untouched.
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Post, "/v1/analytics/event", HttpStatusCode.OK, """{"Result":"Success"}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/kobo/{RecordedDeviceUuid}/v1/analytics/event")
        {
            Content = new StringContent("""{"Events":[]}""", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, Upstream.Requests.Count);
    }

    /// <summary>
    /// Walks the recorded request body and returns the <c>volumeid</c> attached to the first
    /// <c>MarkAsFinished</c> event. Reading the value live instead of hardcoding it keeps the
    /// test in sync when the fixture extractor re-maps UUIDs.
    /// </summary>
    private static Guid ExtractMarkAsFinishedVolumeId(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        foreach (var e in doc.RootElement.GetProperty("Events").EnumerateArray())
        {
            if (e.GetProperty("EventType").GetString() != "MarkAsFinished")
                continue;
            if (!e.TryGetProperty("Attributes", out var attrs) || !attrs.TryGetProperty("volumeid", out var v))
                continue;
            return Guid.Parse(v.GetString()!);
        }
        throw new InvalidOperationException("fixture has no MarkAsFinished event");
    }

    private async Task<Book> SeedLocalBook(Guid entitlementUuid, MediaType mediaType = MediaType.Ebook)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var extension = mediaType == MediaType.Audiobook ? "m4b" : "epub";
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = entitlementUuid,
            Title = "Test Book",
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Test Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = mediaType,
                SourcePath = $"/tmp/{uuid}.{extension}"
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Test Author"]);
        return book;
    }

    /// <summary>
    /// POSTs a synthetic analytics batch with a single event and returns the response. Keeps the
    /// per-event tests terse — no fixture file needed for the simple shapes these tests exercise.
    /// </summary>
    private async Task<HttpResponseMessage> SendSyntheticEvent(string eventType, Guid volumeId, Dictionary<string, string> attributes)
    {
        var attrs = new Dictionary<string, string>(attributes) { ["volumeid"] = volumeId.ToString() };
        var batch = new
        {
            AffiliateName = "Kobo",
            ApplicationVersion = "4.45.23684",
            Events = new[]
            {
                new
                {
                    Attributes = attrs,
                    ClientApplicationVersion = "4.45.23684",
                    EventType = eventType,
                    Id = Guid.NewGuid().ToString(),
                    Metrics = new Dictionary<string, object>(),
                    TestGroups = new Dictionary<string, object>(),
                    Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            }
        };
        var body = JsonSerializer.Serialize(batch);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/kobo/{RecordedDeviceUuid}/v1/analytics/event")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Casts a <c>KoboStoreBookmark.ProgressPercent</c> (declared <c>object?</c> — the Kobo wire format
    /// allows either int or float) to a comparable decimal.
    /// </summary>
    private static decimal? AsDecimal(object? boxed)
    {
        return boxed switch
        {
            null => null,
            decimal d => d,
            int i => i,
            long l => l,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            _ => decimal.Parse(boxed.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    [TestMethod]
    public async Task AnalyticsEvent_MarkAsUnreadForLocalBook_ResetsToReadyToReadAndZeroesProgress()
    {
        var entitlementUuid = Guid.NewGuid();
        await SeedRecordedDevice();
        var book = await SeedLocalBook(entitlementUuid);
        var readingStateRepo = GetService<IReadingStateRepository>();

        // Pre-seed a Finished state (with a non-zero TimesStartedReading) so we can assert that
        // MarkAsUnread resets status + progress without clobbering the lifetime counter.
        var seedTime = DateTimeOffset.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        await readingStateRepo.Upsert(book.BookUuid, new KoboStoreReadingState
        {
            EntitlementId = entitlementUuid.ToString(),
            Created = seedTime,
            LastModified = seedTime,
            PriorityTimestamp = seedTime,
            StatusInfo = new KoboStoreStatusInfo { LastModified = seedTime, Status = "Finished", TimesStartedReading = 2, LastTimeStartedReading = seedTime },
            Statistics = new KoboStoreStatistics { LastModified = seedTime },
            CurrentBookmark = new KoboStoreBookmark { LastModified = seedTime, ProgressPercent = 100, ContentSourceProgressPercent = 100 }
        });

        using var response = await SendSyntheticEvent("MarkAsUnread", entitlementUuid, new Dictionary<string, string>
        {
            ["Origin"] = "MarkAsUnread",
            ["Screen"] = "Library"
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var stored = await readingStateRepo.GetByBookUuid(book.BookUuid);
        Assert.IsNotNull(stored);
        Assert.AreEqual("ReadyToRead", stored.StatusInfo.Status);
        Assert.AreEqual(0m, AsDecimal(stored.CurrentBookmark.ProgressPercent));
        Assert.AreEqual(0m, AsDecimal(stored.CurrentBookmark.ContentSourceProgressPercent));
        Assert.AreEqual(2, stored.StatusInfo.TimesStartedReading);
        Assert.AreEqual(seedTime, stored.StatusInfo.LastTimeStartedReading);
    }

    [TestMethod]
    public async Task AnalyticsEvent_BookProgressForLocalBook_DropsEventWithoutChangingState()
    {
        var entitlementUuid = Guid.NewGuid();
        await SeedRecordedDevice();
        var book = await SeedLocalBook(entitlementUuid, MediaType.Ebook);

        using var response = await SendSyntheticEvent("BookProgress", entitlementUuid, new Dictionary<string, string>
        {
            ["progress"] = "0.5"
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var stored = await GetService<IReadingStateRepository>().GetByBookUuid(book.BookUuid);
        Assert.IsNull(stored, "BookProgress is observational telemetry — PUT /state owns reading state, not analytics");
    }

    [TestMethod]
    public async Task AnalyticsEvent_PlaybackForLocalAudiobook_DropsEventWithoutChangingState()
    {
        var entitlementUuid = Guid.NewGuid();
        await SeedRecordedDevice();
        var book = await SeedLocalBook(entitlementUuid, MediaType.Audiobook);

        using var response = await SendSyntheticEvent("Playback", entitlementUuid, new Dictionary<string, string>
        {
            ["ProgressPercent"] = "0.25",
            ["SecondsListened"] = "120",
            ["PlaybackSpeed"] = "1",
            ["Description"] = "Start",
            ["IsPlaying"] = "Yes"
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var stored = await GetService<IReadingStateRepository>().GetByBookUuid(book.BookUuid);
        Assert.IsNull(stored, "Playback is observational telemetry — PUT /state owns reading state, not analytics");
    }
}
