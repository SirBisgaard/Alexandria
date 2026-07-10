using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Reading progress written outside a device — by another Kobo, an AudioBookShelf pull, or a
/// MarkAsFinished analytics event — bumps only the reading state's ModifiedAt, never the book's.
/// The sync flow must still pick the book up and push a bare ChangedReadingState item, without
/// re-sending the entitlement the device already holds.
/// </summary>
[TestClass]
public sealed class SyncReadingStateOnlyTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task Sync_ExternalReadingStateChange_SendsBareChangedReadingState()
    {
        await SeedRecordedDevice();
        var bookModifiedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var book = await SeedLocalAudiobook(bookModifiedAt);
        var syncRepo = GetService<IKoboDeviceSyncRepository>();
        await syncRepo.UpsertSyncedFile(RecordedDeviceUuid.ToString(), book.BookUuid, book.ModifiedAt);
        await SeedReadingState(book, "Reading");
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");

        // Watermark after the book's ModifiedAt but before the reading-state write above.
        using var response = await SendSync(watermark: bookModifiedAt.AddDays(1));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var items = body.EnumerateArray().ToList();
        Assert.HasCount(1, items);
        Assert.IsTrue(items[0].TryGetProperty("ChangedReadingState", out var changed), "the only sync item must be a bare ChangedReadingState — no entitlement re-send");
        var readingState = changed.GetProperty("ReadingState");
        Assert.AreEqual(book.EntitlementUuid.ToString(), readingState.GetProperty("EntitlementId").GetString());
        Assert.AreEqual("Reading", readingState.GetProperty("StatusInfo").GetProperty("Status").GetString());
    }

    [TestMethod]
    public async Task Sync_ReadingStateOlderThanWatermark_SendsNothing()
    {
        await SeedRecordedDevice();
        var bookModifiedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var book = await SeedLocalAudiobook(bookModifiedAt);
        var syncRepo = GetService<IKoboDeviceSyncRepository>();
        await syncRepo.UpsertSyncedFile(RecordedDeviceUuid.ToString(), book.BookUuid, book.ModifiedAt);
        await SeedReadingState(book, "Reading");
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");

        // Watermark after the reading-state write — the device has already seen this progress.
        using var response = await SendSync(watermark: DateTimeOffset.UtcNow.AddDays(1));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.IsEmpty(body.EnumerateArray().ToList());
    }

    private async Task<HttpResponseMessage> SendSync(DateTimeOffset watermark)
    {
        var tokenService = GetService<IKoboDeviceSyncTokenService>();
        var syncToken = tokenService.GenerateToken(new KoboDeviceSyncToken { LastSync = watermark });

        using var request = new HttpRequestMessage(HttpMethod.Get, DeviceUrl("/v1/library/sync?Filter=ALL&DownloadUrlFilter=Generic,Android&PrioritizeRecentReads=true"));
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-kobo-synctoken", syncToken);
        return await Client.SendAsync(request);
    }

    private async Task SeedReadingState(Book book, string status)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var state = new KoboStoreReadingState
        {
            EntitlementId = book.BookUuid.ToString(),
            Created = now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = now, Status = status, TimesStartedReading = 1 },
            Statistics = new KoboStoreStatistics { LastModified = now, SpentReadingMinutes = 10, RemainingTimeMinutes = 50 },
            CurrentBookmark = new KoboStoreBookmark { LastModified = now, ProgressPercent = 25 }
        };
        await GetService<IReadingStateRepository>().Upsert(book.BookUuid, state);
    }

    private async Task<Book> SeedLocalAudiobook(DateTimeOffset modifiedAt)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = "Test Audiobook",
            Language = "en",
            Description = "Test description",
            Publisher = "Test Publisher",
            PublicationDate = "2026",
            CreatedAt = modifiedAt,
            ModifiedAt = modifiedAt,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Test Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Audiobook,
                SourcePath = $"/tmp/{uuid}.m4b",
                Duration = 3600,
                AudioBitrate = 64000,
                TotalFileSize = 1_000_000
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Test Author"]);
        return book;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
