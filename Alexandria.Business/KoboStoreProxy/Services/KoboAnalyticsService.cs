using System.Globalization;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboAnalyticsService(IKoboProxyService koboProxyService, IBookService bookService, IReadingStateRepository readingStateRepository, IBookActivityEventRepository activityEventRepository, ILogger<KoboAnalyticsService> logger) : IKoboAnalyticsService
{
    /// <summary>
    /// Intercepts analytics events to handle local book state changes. Events are split: local book
    /// events are handled here; store-purchased book events are collected and proxied to Kobo.
    /// Handled local event types:
    ///   - <c>MarkAsFinished</c>/<c>MarkAsUnread</c>: user-driven status transitions the device only
    ///     expresses through analytics (no PUT-state equivalent).
    ///   - <c>Playback</c> (audiobooks): <c>ProgressPercent</c> attribute (decimal 0–1) scaled to 0–100.
    ///     Fills the offline-listening gap PUT-state leaves because state PUTs only fire on book close.
    ///   - <c>OpenContent</c>/<c>LeaveContent</c> (ebooks): <c>progress</c> attribute (integer 0–100),
    ///     used as-is. <c>BookProgress</c> looks similar but its <c>progress</c> is chapter-relative
    ///     (every observed prod event reports exactly 50) so it's deliberately not handled here.
    /// Other local event types (TableOfContents, OpenPlayerScreen, etc.) are observational and dropped.
    /// All progress writes go through <see cref="ReadingStateRepository.Upsert"/>, which silently no-ops
    /// when the incoming <c>LastModified</c> is older than the stored value — that's the newest-wins
    /// guard preventing analytics from clobbering a fresher PUT and vice versa.
    /// When all events are local, returns 200 OK with empty body (device expects success and doesn't
    /// inspect content). Mixed batches are rebuilt with only store events before proxying —
    /// <c>Content-Length</c> header removed so it recalculates for the new body size.
    /// </summary>
    public async Task<KoboProxyResponse> AnalyticsEvent(KoboProxyRequest request)
    {
        KoboStoreAnalyticsRequest? analyticsRequest;
        try
        {
            analyticsRequest = JsonSerializer.Deserialize<KoboStoreAnalyticsRequest>(request.Body);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "AnalyticsEvent: failed to deserialize request body, proxying unchanged");
            return await koboProxyService.Proxy(request);
        }

        if (analyticsRequest?.Events is null or { Count: 0 })
            return await koboProxyService.Proxy(request);

        var eventsToProxy = new List<KoboStoreAnalyticsEvent>();

        foreach (var analyticsEvent in analyticsRequest.Events)
        {
            var volumeId = GetVolumeId(analyticsEvent);
            if (volumeId is null)
            {
                eventsToProxy.Add(analyticsEvent);
                continue;
            }

            var book = await bookService.GetBookByEntitlementUuid(volumeId.Value);
            if (book is null)
            {
                eventsToProxy.Add(analyticsEvent);
                continue;
            }

            await PersistActivityEvent(analyticsEvent, book.BookUuid, request.DeviceKey);

            switch (analyticsEvent.EventType)
            {
                case "MarkAsFinished":
                    logger.LogInformation("AnalyticsEvent: handling MarkAsFinished for '{Title}' ({Uuid})", book.Title, book.EntitlementUuid);
                    await HandleMarkAsFinished(book.BookUuid, book.EntitlementUuid.ToString(), analyticsEvent.Timestamp);
                    break;
                case "MarkAsUnread":
                    logger.LogInformation("AnalyticsEvent: handling MarkAsUnread for '{Title}' ({Uuid})", book.Title, book.EntitlementUuid);
                    await HandleMarkAsUnread(book.BookUuid, book.EntitlementUuid.ToString(), analyticsEvent.Timestamp);
                    break;
                case "Playback":
                    await HandleAudiobookPlayback(book, analyticsEvent);
                    break;
                case "OpenContent":
                case "LeaveContent":
                    await HandleEbookProgress(book, analyticsEvent);
                    break;
                default:
                    logger.LogInformation("AnalyticsEvent: dropping {EventType} for local book '{Title}'", analyticsEvent.EventType, book.Title);
                    break;
            }
        }

        if (eventsToProxy.Count == 0)
            return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new object());

        var filteredRequest = analyticsRequest with { Events = eventsToProxy };
        var filteredBody = JsonSerializer.SerializeToUtf8Bytes(filteredRequest);
        var headers = new Dictionary<string, string?[]>(request.Headers, StringComparer.OrdinalIgnoreCase);
        headers.Remove("Content-Length");
        var proxyRequest = new KoboProxyRequest
        {
            Method = request.Method,
            Path = request.Path,
            QueryString = request.QueryString,
            Headers = headers,
            Body = filteredBody
        };

        return await koboProxyService.Proxy(proxyRequest);
    }

    private async Task HandleMarkAsFinished(Guid bookUuid, string entitlementId, string? timestamp)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventTime = timestamp ?? now;
        var existing = await readingStateRepository.GetByBookUuid(bookUuid);

        var timesStarted = existing?.StatusInfo.TimesStartedReading ?? 1;
        var lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading;

        if (existing is null)
            lastTimeStarted = eventTime;

        var state = new KoboStoreReadingState
        {
            EntitlementId = entitlementId,
            Created = existing?.Created ?? eventTime,
            LastModified = eventTime,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = eventTime, Status = "Finished", TimesStartedReading = timesStarted, LastTimeStartedReading = lastTimeStarted },
            Statistics = new KoboStoreStatistics { LastModified = existing?.Statistics.LastModified ?? eventTime, SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes, RemainingTimeMinutes = 0 },
            CurrentBookmark = new KoboStoreBookmark { LastModified = eventTime, ProgressPercent = 100, ContentSourceProgressPercent = existing?.CurrentBookmark.ContentSourceProgressPercent ?? 100 }
        };

        await readingStateRepository.Upsert(bookUuid, state);
    }

    private async Task HandleMarkAsUnread(Guid bookUuid, string entitlementId, string? timestamp)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventTime = timestamp ?? now;
        var existing = await readingStateRepository.GetByBookUuid(bookUuid);

        var state = new KoboStoreReadingState
        {
            EntitlementId = entitlementId,
            Created = existing?.Created ?? eventTime,
            LastModified = eventTime,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = eventTime, Status = "ReadyToRead", TimesStartedReading = existing?.StatusInfo.TimesStartedReading ?? 0, LastTimeStartedReading = existing?.StatusInfo.LastTimeStartedReading },
            Statistics = new KoboStoreStatistics { LastModified = existing?.Statistics.LastModified ?? eventTime, SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes, RemainingTimeMinutes = existing?.Statistics.RemainingTimeMinutes },
            CurrentBookmark = new KoboStoreBookmark { LastModified = eventTime, ProgressPercent = 0, ContentSourceProgressPercent = 0, Location = null }
        };

        await readingStateRepository.Upsert(bookUuid, state);
    }

    private async Task HandleAudiobookPlayback(Book book, KoboStoreAnalyticsEvent analyticsEvent)
    {
        if (book.File.MediaType != MediaType.Audiobook)
        {
            logger.LogInformation("AnalyticsEvent: dropping Playback for non-audiobook '{Title}'", book.Title);
            return;
        }
        var fraction = ParseDecimalAttribute(analyticsEvent, "ProgressPercent");
        if (fraction is null)
        {
            logger.LogInformation("AnalyticsEvent: Playback for '{Title}' missing or unparseable ProgressPercent, skipping", book.Title);
            return;
        }
        var progressPercent = fraction.Value * 100m;
        logger.LogInformation("AnalyticsEvent: Playback for '{Title}' ({Uuid}) → {Progress}%", book.Title, book.EntitlementUuid, progressPercent);
        await UpsertProgressFromAnalytics(book, progressPercent, analyticsEvent.Timestamp);
    }

    private async Task HandleEbookProgress(Book book, KoboStoreAnalyticsEvent analyticsEvent)
    {
        if (book.File.MediaType == MediaType.Audiobook)
        {
            logger.LogInformation("AnalyticsEvent: dropping {EventType} for audiobook '{Title}'", analyticsEvent.EventType, book.Title);
            return;
        }
        var integerPercent = ParseDecimalAttribute(analyticsEvent, "progress");
        if (integerPercent is null)
        {
            logger.LogInformation("AnalyticsEvent: {EventType} for '{Title}' missing or unparseable progress, skipping", analyticsEvent.EventType, book.Title);
            return;
        }
        logger.LogInformation("AnalyticsEvent: {EventType} for '{Title}' ({Uuid}) → {Progress}%", analyticsEvent.EventType, book.Title, book.EntitlementUuid, integerPercent.Value);
        await UpsertProgressFromAnalytics(book, integerPercent.Value, analyticsEvent.Timestamp);
    }

    /// <summary>
    /// Shared upsert path for analytics-derived progress. Preserves fields analytics doesn't carry
    /// (Location, RemainingTimeMinutes, SpentReadingMinutes) by reading the existing row first and
    /// only swapping in the new ProgressPercent + timestamps. Status is bumped to Reading on the
    /// first non-Reading→Reading transition (and TimesStartedReading incremented). For audiobooks
    /// ContentSourceProgressPercent is preserved because it's chapter-relative and analytics carries
    /// only book-level percent; for ebooks the two are equal so we mirror ProgressPercent. The
    /// newest-wins guard inside the repository's Upsert prevents this write from clobbering a
    /// fresher PUT-state row.
    /// </summary>
    private async Task UpsertProgressFromAnalytics(Book book, decimal progressPercent, string? timestamp)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventTime = timestamp ?? now;
        var existing = await readingStateRepository.GetByBookUuid(book.BookUuid);

        var newStatus = existing?.StatusInfo.Status == "Finished" ? "Finished" : "Reading";
        var timesStarted = existing?.StatusInfo.TimesStartedReading ?? 0;
        var lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading;
        if (newStatus == "Reading" && existing?.StatusInfo.Status != "Reading")
        {
            timesStarted++;
            lastTimeStarted = eventTime;
        }

        var contentSourcePercent = book.File.MediaType == MediaType.Audiobook
            ? existing?.CurrentBookmark.ContentSourceProgressPercent
            : progressPercent;

        var state = new KoboStoreReadingState
        {
            EntitlementId = book.EntitlementUuid.ToString(),
            Created = existing?.Created ?? eventTime,
            LastModified = eventTime,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = eventTime, Status = newStatus, TimesStartedReading = timesStarted, LastTimeStartedReading = lastTimeStarted },
            Statistics = new KoboStoreStatistics { LastModified = existing?.Statistics.LastModified ?? eventTime, SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes, RemainingTimeMinutes = existing?.Statistics.RemainingTimeMinutes },
            CurrentBookmark = new KoboStoreBookmark { LastModified = eventTime, ProgressPercent = progressPercent, ContentSourceProgressPercent = contentSourcePercent, Location = existing?.CurrentBookmark.Location }
        };

        await readingStateRepository.Upsert(book.BookUuid, state);
    }

    private static decimal? ParseDecimalAttribute(KoboStoreAnalyticsEvent analyticsEvent, string key)
    {
        if (analyticsEvent.Attributes is null)
            return null;
        if (!analyticsEvent.Attributes.TryGetValue(key, out var raw) || raw is null)
            return null;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static Guid? GetVolumeId(KoboStoreAnalyticsEvent analyticsEvent)
    {
        if (analyticsEvent.Attributes is null)
            return null;
        if (!analyticsEvent.Attributes.TryGetValue("volumeid", out var volumeIdStr))
            return null;
        if (Guid.TryParse(volumeIdStr, out var volumeId))
            return volumeId;
        return null;
    }

    /// <summary>
    /// Persists a single analytics event scoped to a local book. EventId is the Kobo-assigned guid
    /// — devices retry buffered analytics batches when coming back online, so the same event id
    /// can arrive multiple times. The repository uses INSERT OR IGNORE on the EventId PK to dedupe.
    /// Missing id/timestamp fall back to fresh values rather than dropping the event, so a partial
    /// payload still produces a row.
    /// </summary>
    private async Task PersistActivityEvent(KoboStoreAnalyticsEvent analyticsEvent, Guid bookUuid, string? deviceKey)
    {
        if (string.IsNullOrEmpty(analyticsEvent.EventType))
            return;

        var receivedAt = DateTimeOffset.UtcNow;
        var eventId = string.IsNullOrEmpty(analyticsEvent.Id) ? Guid.NewGuid().ToString() : analyticsEvent.Id;
        var eventTimestamp = DateTimeOffset.TryParse(analyticsEvent.Timestamp, out var parsed) ? parsed : receivedAt;
        var body = JsonSerializer.Serialize(analyticsEvent);

        await activityEventRepository.Insert(new BookActivityEvent
        {
            EventId = eventId,
            BookUuid = bookUuid,
            DeviceUuid = deviceKey ?? "",
            EventType = analyticsEvent.EventType,
            EventTimestamp = eventTimestamp,
            Body = body,
            ReceivedAt = receivedAt
        });
    }
}
