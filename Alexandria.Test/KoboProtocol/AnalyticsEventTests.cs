using System.Net;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/analytics/event receives a bag of device telemetry events. Alexandria only actually cares
/// about <c>MarkAsFinished</c> for local books — it updates reading state to Finished. All other
/// local event types (BookOpened, Playback, StartReadingBook, etc.) are dropped as cosmetic.
/// Events for books Alexandria doesn't own, and events with no volume id at all, are forwarded
/// to Kobo so the store can keep its own analytics. A mixed batch triggers a filtered-forward:
/// the proxied request body contains only the non-local events, with <c>Content-Length</c>
/// stripped so the HttpClient recomputes it.
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

    private async Task<Book> SeedLocalBook(Guid entitlementUuid)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
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
                MediaType = MediaType.Ebook,
                SourcePath = $"/tmp/{uuid}.epub"
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Test Author"]);
        return book;
    }
}
