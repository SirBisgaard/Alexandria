using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboAnalyticsService(IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, IReadingStateRepository readingStateRepository, ILogger<KoboAnalyticsService> logger) : IKoboAnalyticsService
{
    /// <summary>
    /// Intercepts analytics events to handle local book state changes. Events are split: local book
    /// events are handled here; store-purchased book events are collected and proxied to Kobo.
    /// Only MarkAsFinished is handled for local books — all other local event types (BookOpened, etc.)
    /// are dropped as they're cosmetic with no local-side effect. When all events are local, returns
    /// 200 OK with empty body (device expects success and doesn't inspect content). Mixed batches
    /// are rebuilt with only store events before proxying — Content-Length header removed so it
    /// recalculates for the new body size.
    /// </summary>
    public async Task<KoboProxyResponse> AnalyticsEvent(KoboProxyRequest request)
    {
        KoboStoreAnalyticsRequest? analyticsRequest;
        try
        {
            analyticsRequest = JsonSerializer.Deserialize<KoboStoreAnalyticsRequest>(request.Body);
        }
        catch (JsonException)
        {
            logger.LogWarning("AnalyticsEvent: failed to deserialize request body, proxying unchanged");
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

            var book = await alexandriaLibraryService.GetBookByEntitlementUuid(volumeId.Value);
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
