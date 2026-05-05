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

public class KoboAnalyticsService(IKoboProxyService koboProxyService, IBookService bookService, IReadingStateRepository readingStateRepository, ILogger<KoboAnalyticsService> logger) : IKoboAnalyticsService
{
    /// <summary>
    /// Intercepts analytics events to handle local book state changes. Events are split: local book
    /// events are handled here; store-purchased book events are collected and proxied to Kobo.
    /// Handled local event types: MarkAsFinished, MarkAsUnread, BookProgress (ebook progress percent
    /// from Attributes["progress"]), and Playback (audiobook progress percent from
    /// Attributes["ProgressPercent"]). All other local event types (BookOpened, etc.) are dropped as
    /// cosmetic with no local-side effect. When all events are local, returns 200 OK with empty body
    /// (device expects success and doesn't inspect content). Mixed batches are rebuilt with only
    /// store events before proxying — Content-Length header removed so it recalculates for the new
    /// body size.
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
                case "BookProgress":
                    await HandleBookProgress(book, analyticsEvent);
                    break;
                case "Playback":
                    await HandlePlayback(book, analyticsEvent);
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

    private async Task HandleBookProgress(Book book, KoboStoreAnalyticsEvent analyticsEvent)
    {
        var progress = ParseDecimalAttribute(analyticsEvent, "progress");
        if (progress is null)
        {
            logger.LogInformation("AnalyticsEvent: BookProgress for '{Title}' missing or unparseable 'progress' attribute, skipping", book.Title);
            return;
        }
        logger.LogInformation("AnalyticsEvent: handling BookProgress for '{Title}' ({Uuid}) at {Progress}%", book.Title, book.EntitlementUuid, progress);
        await UpsertProgress(book, progress.Value, analyticsEvent.Timestamp);
    }

    private async Task HandlePlayback(Book book, KoboStoreAnalyticsEvent analyticsEvent)
    {
        if (book.File.MediaType != MediaType.Audiobook)
        {
            logger.LogInformation("AnalyticsEvent: dropping Playback for non-audiobook '{Title}'", book.Title);
            return;
        }
        var progress = ParseDecimalAttribute(analyticsEvent, "ProgressPercent");
        if (progress is null)
        {
            logger.LogInformation("AnalyticsEvent: Playback for '{Title}' missing or unparseable 'ProgressPercent' attribute, skipping", book.Title);
            return;
        }
        logger.LogInformation("AnalyticsEvent: handling Playback for '{Title}' ({Uuid}) at {Progress}%", book.Title, book.EntitlementUuid, progress);
        await UpsertProgress(book, progress.Value, analyticsEvent.Timestamp);
    }

    /// <summary>
    /// Shared progress-update path for BookProgress and Playback events. Mirrors the Reading-transition
    /// rule from <see cref="KoboReadingStateService"/>: when status moves from non-Reading to Reading,
    /// increment TimesStartedReading and stamp LastTimeStartedReading. Status is never auto-bumped to
    /// Finished — that transition is owned by the separate MarkAsFinished event.
    /// </summary>
    private async Task UpsertProgress(Book book, decimal progressPercent, string? timestamp)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventTime = timestamp ?? now;
        var existing = await readingStateRepository.GetByBookUuid(book.BookUuid);

        var timesStarted = existing?.StatusInfo.TimesStartedReading ?? 0;
        var lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading;
        var newStatus = existing?.StatusInfo.Status == "Finished" ? "Finished" : "Reading";

        if (newStatus == "Reading" && (existing is null || existing.StatusInfo.Status != "Reading"))
        {
            timesStarted++;
            lastTimeStarted = eventTime;
        }

        var state = new KoboStoreReadingState
        {
            EntitlementId = book.EntitlementUuid.ToString(),
            Created = existing?.Created ?? eventTime,
            LastModified = eventTime,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = eventTime, Status = newStatus, TimesStartedReading = timesStarted, LastTimeStartedReading = lastTimeStarted },
            Statistics = new KoboStoreStatistics { LastModified = existing?.Statistics.LastModified ?? eventTime, SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes, RemainingTimeMinutes = existing?.Statistics.RemainingTimeMinutes },
            CurrentBookmark = new KoboStoreBookmark { LastModified = eventTime, ProgressPercent = progressPercent, ContentSourceProgressPercent = progressPercent, Location = existing?.CurrentBookmark.Location }
        };

        await readingStateRepository.Upsert(book.BookUuid, state);
    }

    private static decimal? ParseDecimalAttribute(KoboStoreAnalyticsEvent analyticsEvent, string key)
    {
        if (analyticsEvent.Attributes is null)
            return null;
        if (!analyticsEvent.Attributes.TryGetValue(key, out var raw) || raw is null)
            return null;
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return null;
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
}
