using System.Text.Json;
using System.Text.Json.Nodes;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboLibrarySyncService(AlexandriaSettings alexandriaSettings, IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, IAlexandriaSettingsService settingsService, IReadingStateRepository readingStateRepository, IKoboDeviceSyncRepository koboDeviceSyncRepository, IKoboDeviceSyncTokenService koboDeviceSyncTokenService, ILogger<KoboLibrarySyncService> logger) : IKoboLibrarySyncService
{
    public async Task<KoboProxyResponse> Initialization(KoboProxyRequest request)
    {
        logger.LogInformation("Initialization: rewriting resource URLs to route through Alexandria");
        var koboResponse = await koboProxyService.Proxy(request);

        var jsonNode = JsonSerializer.Deserialize<JsonNode>(koboResponse.Body);
        if (jsonNode is not JsonObject json)
            return koboResponse;

        var resources = json["Resources"]?.AsObject();
        if (resources is null)
            return koboResponse;

        resources["library_sync"] = $"{alexandriaSettings.KoboApiUrl}/v1/library/sync";
        resources["image_host"] = $"{alexandriaSettings.KoboApiUrl}";
        resources["image_url_template"] = $"{alexandriaSettings.KoboApiUrl}/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg";
        resources["image_url_quality_template"] = $"{alexandriaSettings.KoboApiUrl}/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg";

        var headers = new Dictionary<string, string[]> { { "x-kobo-apitoken", ["e30="] } };
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, json!, headers);
    }

    public async Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request)
    {
        logger.LogInformation("LibrarySync: fetching store results and merging local books");
        var deviceId = GetDeviceId(request);
        var alexandriaToken = koboDeviceSyncTokenService.ParseToken(GetSyncTokenHeader(request));

        request.Headers["x-kobo-synctoken"] = [alexandriaToken.KoboToken];
        var koboResponse = await koboProxyService.Proxy(request);

        var koboResponseToken = GetResponseSyncToken(koboResponse) ?? alexandriaToken.KoboToken;

        if (HasKoboContinuation(koboResponse))
        {
            koboDeviceSyncTokenService.Clear(deviceId);
            koboResponse.Headers["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = alexandriaToken.LastSync })];
            logger.LogInformation("LibrarySync: Kobo continuation active, passing through store results");
            return koboResponse;
        }

        var syncResults = new List<KoboStoreSyncResultItem>();
        var storeCount = 0;

        if (koboResponse.StatusCode == 200)
        {
            var storeResults = JsonSerializer.Deserialize<List<KoboStoreSyncResultItem>>(koboResponse.Body);
            if (storeResults is not null)
            {
                storeCount = storeResults.Count;
                syncResults.AddRange(storeResults);
                await PersistStoreCollections(storeResults);
            }
        }

        var allBooks = await alexandriaLibraryService.GetBooks();
        var syncedHashes = await koboDeviceSyncRepository.GetSyncedFileHashes(deviceId);

        if (!koboDeviceSyncTokenService.IsActive(deviceId))
        {
            var lastSync = alexandriaToken.LastSync;
            var books = allBooks.Where(b => b.ModifiedAt > lastSync || !syncedHashes.ContainsKey(b.BookUuid)).ToList();
            if (books.Count == 0)
            {
                var collections = await GetCollectionSyncItems(lastSync, allBooks);
                syncResults.AddRange(collections);
                logger.LogInformation("LibrarySync: returning {Store} store items + {Collections} collections, no new/changed local books since {LastSync}", storeCount, collections.Count, lastSync);
                var headers = new Dictionary<string, string[]>(koboResponse.Headers);
                headers.Remove("x-kobo-sync");
                headers["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
                return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, syncResults, headers);
            }

            logger.LogInformation("LibrarySync: {Count} local books to sync since {LastSync}", books.Count, lastSync);
            koboDeviceSyncTokenService.Start(deviceId, books.Select(b => b.BookUuid).ToList());
        }

        var workGroupIds = BuildWorkGroupLookup(allBooks);

        var appSettings = await settingsService.Get();
        var batchSize = appSettings.SyncBatchSize;
        var batchUuids = koboDeviceSyncTokenService.GetNextBatch(deviceId, batchSize);
        var hasMore = koboDeviceSyncTokenService.HasMore(deviceId);

        foreach (var uuid in batchUuids)
        {
            var book = await alexandriaLibraryService.GetBookByUuid(uuid);
            if (book is null)
                continue;

            var previouslySynced = syncedHashes.TryGetValue(book.BookUuid, out var storedHash);

            if (!previouslySynced)
            {
                if (book.IsArchived)
                    continue;

                var readingState = await readingStateRepository.GetByBookUuid(book.BookUuid);
                if (readingState is not null)
                    readingState = readingState with { EntitlementId = book.EntitlementUuid.ToString() };

                var relatedGroupId = GetRelatedGroupId(book, workGroupIds);
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, alexandriaSettings.KoboApiUrl, readingState, relatedGroupId);
                syncResults.Add(new KoboStoreSyncResultItem { NewEntitlement = entitlement });
                await koboDeviceSyncRepository.UpsertSyncedFile(deviceId, book.BookUuid, book.FileHash);
            }
            else if (book.IsArchived)
            {
                var entitlement = KoboStoreEntityMapper.WithRemoved(KoboStoreEntityMapper.ToStoreEntitlement(book, alexandriaSettings.KoboApiUrl));
                syncResults.Add(new KoboStoreSyncResultItem { ChangedEntitlement = entitlement });
                await koboDeviceSyncRepository.RemoveSyncedFile(deviceId, book.BookUuid);
            }
            else if (storedHash != book.FileHash)
            {
                var readingState = await readingStateRepository.GetByBookUuid(book.BookUuid);
                if (readingState is not null)
                    readingState = readingState with { EntitlementId = book.EntitlementUuid.ToString() };

                var relatedGroupId = GetRelatedGroupId(book, workGroupIds);
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, alexandriaSettings.KoboApiUrl, relatedGroupId: relatedGroupId);
                var removedEntitlement = KoboStoreEntityMapper.WithRemoved(entitlement);
                syncResults.Add(new KoboStoreSyncResultItem { ChangedEntitlement = removedEntitlement });
                syncResults.Add(new KoboStoreSyncResultItem { NewEntitlement = entitlement with { ReadingState = null } });
                if (readingState is not null)
                    syncResults.Add(new KoboStoreSyncResultItem { ChangedReadingState = new KoboStoreChangedReadingState { ReadingState = readingState } });
                await koboDeviceSyncRepository.UpsertSyncedFile(deviceId, book.BookUuid, book.FileHash);
            }
            else
            {
                var readingState = await readingStateRepository.GetByBookUuid(book.BookUuid);
                if (readingState is not null)
                    readingState = readingState with { EntitlementId = book.EntitlementUuid.ToString() };

                var relatedGroupId = GetRelatedGroupId(book, workGroupIds);
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, alexandriaSettings.KoboApiUrl, relatedGroupId: relatedGroupId);
                syncResults.Add(new KoboStoreSyncResultItem { ChangedEntitlement = entitlement });
                if (readingState is not null)
                    syncResults.Add(new KoboStoreSyncResultItem { ChangedReadingState = new KoboStoreChangedReadingState { ReadingState = readingState } });
            }
        }

        var responseHeaders = new Dictionary<string, string[]>(koboResponse.Headers);

        if (hasMore)
        {
            responseHeaders["x-kobo-sync"] = ["continue"];
            responseHeaders["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = alexandriaToken.LastSync })];
            logger.LogInformation("LibrarySync: returning {Total} items ({Local} local, {Store} store), more local books pending", syncResults.Count, batchUuids.Count, storeCount);
        }
        else
        {
            var lastSyncTime = alexandriaToken.LastSync;
            var collections = await GetCollectionSyncItems(lastSyncTime, allBooks);
            syncResults.AddRange(collections);
            responseHeaders.Remove("x-kobo-sync");
            responseHeaders["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
            koboDeviceSyncTokenService.Clear(deviceId);
            logger.LogInformation("LibrarySync: returning final {Total} items ({Local} local, {Store} store, {Collections} collections)", syncResults.Count, batchUuids.Count, storeCount, collections.Count);
        }

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, syncResults, responseHeaders);
    }

    public async Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("Metadata: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }
        var allBooks = await alexandriaLibraryService.GetBooks();
        var relatedGroupId = GetRelatedGroupId(book, BuildWorkGroupLookup(allBooks));

        logger.LogInformation("Metadata: serving metadata for '{Title}' ({Uuid})", book.Title, uuid);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new[] { KoboStoreEntityMapper.ToMetadata(book, alexandriaSettings.KoboApiUrl, relatedGroupId) });
    }

    public async Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("DeleteBook: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        logger.LogInformation("DeleteBook: archiving '{Title}' ({Uuid})", book.Title, uuid);
        await alexandriaLibraryService.SetBookArchiveStatus(book.BookUuid, true);

        return new KoboProxyResponse { StatusCode = StatusCodes.Status204NoContent, Headers = new Dictionary<string, string[]>(), Body = [] };
    }

    private static string GetDeviceId(KoboProxyRequest request)
    {
        if (request.Headers.TryGetValue("x-kobo-deviceid", out var values) && values.Length > 0 && values[0] is not null)
            return values[0]!;
        return "unknown";
    }

    private static string? GetSyncTokenHeader(KoboProxyRequest request) => 
        request.Headers.TryGetValue("x-kobo-synctoken", out var values) && values.Length > 0 ? values[0] : null;

    private static string? GetResponseSyncToken(KoboProxyResponse response) => 
        response.Headers.TryGetValue("x-kobo-synctoken", out var values) && values.Length > 0 ? values[0] : null;

    private static bool HasKoboContinuation(KoboProxyResponse response) => response.Headers.TryGetValue("x-kobo-sync", out var values) && values.Any(v => string.Equals(v, "continue", StringComparison.OrdinalIgnoreCase));

    private async Task<List<KoboStoreSyncResultItem>> GetCollectionSyncItems(DateTimeOffset lastSync, List<Book> allBooks)
    {
        var collections = lastSync == DateTimeOffset.MinValue ? await alexandriaLibraryService.GetCollections() : await alexandriaLibraryService.GetCollectionsModifiedSince(lastSync);
        var entitlementLookup = allBooks.ToDictionary(b => b.BookUuid, b => b.EntitlementUuid);

        return collections.Select(c =>
        {
            var isNew = c.CreatedAt > lastSync;
            var tag = new KoboStoreTagContainer
            {
                Tag = new KoboStoreTag
                {
                    Id = c.CollectionUuid.ToString(),
                    Name = c.Name,
                    Created = c.CreatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    LastModified = c.ModifiedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Items = c.BookUuids.Select(id => new KoboStoreTagItem { RevisionId = (entitlementLookup.TryGetValue(id, out var eid) ? eid : id).ToString() }).ToList()
                }
            };
            return isNew ? new KoboStoreSyncResultItem { NewTag = tag } : new KoboStoreSyncResultItem { ChangedTag = tag };
        }).ToList();
    }

    private async Task PersistStoreCollections(List<KoboStoreSyncResultItem> storeResults)
    {
        foreach (var item in storeResults)
        {
            var tag = (item.NewTag ?? item.ChangedTag)?.Tag;
            if (tag is null)
                continue;

            var collection = new BookCollection
            {
                CollectionUuid = Guid.Parse(tag.Id),
                Name = tag.Name,
                BookUuids = tag.Items?.Select(i => Guid.Parse(i.RevisionId)).ToList() ?? [],
                CreatedAt = DateTimeOffset.TryParse(tag.Created, out var created) ? created : DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.TryParse(tag.LastModified, out var modified) ? modified : DateTimeOffset.UtcNow
            };

            await alexandriaLibraryService.UpsertCollection(collection);
            logger.LogInformation("LibrarySync: persisted store collection '{Name}' ({Uuid}) with {Count} books", collection.Name, collection.CollectionUuid, collection.BookUuids.Count);
        }
    }

    private static HashSet<Guid> BuildWorkGroupLookup(List<Book> books)
    {
        return books
            .GroupBy(b => b.GenerateWorkUuid())
            .Where(g => g.Select(b => b.MediaType).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
    }

    private static Guid? GetRelatedGroupId(Book book, HashSet<Guid> workGroupIds)
    {
        var workUuid = book.GenerateWorkUuid();
        return workGroupIds.Contains(workUuid) ? workUuid : null;
    }

}
