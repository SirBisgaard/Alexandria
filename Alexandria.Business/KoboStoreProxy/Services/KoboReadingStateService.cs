using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboReadingStateService(IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, IReadingStateRepository readingStateRepository, ILogger<KoboReadingStateService> logger) : IKoboReadingStateService
{
    public async Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("ReadingState: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }
        logger.LogInformation("ReadingState: handling {Method} for '{Title}' ({Uuid})", request.Method, book.Title, uuid);

        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entitlementUuidStr = book.EntitlementUuid.ToString();

        if (request.Method == "PUT")
            return await HandleReadingStatePut(request, book, entitlementUuidStr, now);

        var storedState = await readingStateRepository.GetByBookUuid(book.BookUuid);
        if (storedState is not null)
        {
            storedState = storedState with { EntitlementId = entitlementUuidStr };
            return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new[] { storedState });
        }

        var defaultState = new KoboStoreReadingState
        {
            EntitlementId = entitlementUuidStr,
            Created = book.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = now, Status = "ReadyToRead" },
            Statistics = new KoboStoreStatistics { LastModified = now },
            CurrentBookmark = new KoboStoreBookmark { LastModified = now }
        };

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new[] { defaultState });
    }

    private async Task<KoboProxyResponse> HandleReadingStatePut(KoboProxyRequest request, Book book, string entitlementUuidStr, string now)
    {
        var rawBody = Encoding.UTF8.GetString(request.Body);
        var putRequest = JsonSerializer.Deserialize<KoboStoreReadingStatePutRequest>(rawBody);
        var item = putRequest?.ReadingStates?.FirstOrDefault();

        var putResponse = new { RequestResult = "Success", UpdateResults = new[] { new { EntitlementId = entitlementUuidStr, CurrentBookmarkResult = new { Result = "Success" }, StatisticsResult = new { Result = "Success" }, StatusInfoResult = new { Result = "Success" }, LastModified = now, PriorityTimestamp = now } } };

        if (item is not null)
        {
            var existing = await readingStateRepository.GetByBookUuid(book.BookUuid);

            if (existing is not null && item.LastModified is not null && DateTimeOffset.Parse(item.LastModified) < DateTimeOffset.Parse(existing.LastModified))
            {
                logger.LogInformation("ReadingState: rejecting stale PUT for '{Title}' (incoming: {Incoming}, stored: {Stored})", book.Title, item.LastModified, existing.LastModified);
                return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, putResponse);
            }

            var timesStarted = existing?.StatusInfo.TimesStartedReading ?? 0;
            string? lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading;
            var newStatus = item.StatusInfo?.Status ?? existing?.StatusInfo.Status ?? "ReadyToRead";

            if (newStatus == "Reading" && (existing is null || existing.StatusInfo.Status != "Reading"))
            {
                timesStarted++;
                lastTimeStarted = now;
            }

            var state = new KoboStoreReadingState
            {
                EntitlementId = entitlementUuidStr,
                Created = existing?.Created ?? book.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                LastModified = item.LastModified ?? now,
                PriorityTimestamp = now,
                StatusInfo = new KoboStoreStatusInfo { LastModified = item.StatusInfo?.LastModified ?? now, Status = newStatus, TimesStartedReading = timesStarted, LastTimeStartedReading = lastTimeStarted },
                Statistics = new KoboStoreStatistics { LastModified = item.Statistics?.LastModified ?? now, SpentReadingMinutes = item.Statistics?.SpentReadingMinutes ?? existing?.Statistics.SpentReadingMinutes, RemainingTimeMinutes = item.Statistics?.RemainingTimeMinutes ?? existing?.Statistics.RemainingTimeMinutes },
                CurrentBookmark = new KoboStoreBookmark { LastModified = item.CurrentBookmark?.LastModified ?? now, ProgressPercent = item.CurrentBookmark?.ProgressPercent ?? existing?.CurrentBookmark.ProgressPercent, ContentSourceProgressPercent = item.CurrentBookmark?.ContentSourceProgressPercent ?? existing?.CurrentBookmark.ContentSourceProgressPercent, Location = item.CurrentBookmark?.Location ?? existing?.CurrentBookmark.Location }
            };

            await readingStateRepository.Upsert(book.BookUuid, state);
        }

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, putResponse);
    }
}
