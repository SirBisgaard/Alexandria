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

public class KoboLibrarySyncService(AlexandriaSettings alexandriaSettings, IKoboProxyService koboProxyService, IBookService bookService, ICollectionService collectionService, ISettingsService settingsService, IReadingStateRepository readingStateRepository, IKoboDeviceSyncRepository koboDeviceSyncRepository, IKoboDeviceRepository koboDeviceRepository, IKoboDeviceSyncTokenService koboDeviceSyncTokenService, ILogger<KoboLibrarySyncService> logger) : IKoboLibrarySyncService
{
    /// <summary>
    /// Rewrites 4 resource URLs (library_sync, image_host, image_url_template, image_url_quality_template)
    /// in Kobo's initialization response so the device routes sync/image/download requests through Alexandria.
    /// All other resource URLs are left unchanged. Response includes x-kobo-apitoken header set to "e30="
    /// (base64 for empty JSON "{}") — Kobo requires a non-empty token but doesn't validate contents.
    /// </summary>
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

        var deviceBaseUrl = $"{alexandriaSettings.KoboApiUrl}/{request.DeviceKey}";
        resources["library_sync"] = $"{deviceBaseUrl}/v1/library/sync";
        resources["image_host"] = $"{deviceBaseUrl}";
        resources["image_url_template"] = $"{deviceBaseUrl}/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg";
        resources["image_url_quality_template"] = $"{deviceBaseUrl}/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg";

        var headers = new Dictionary<string, string[]> { { "x-kobo-apitoken", ["e30="] } };
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, json, headers);
    }

    /// <summary>
    /// Merges local Alexandria books into Kobo's library sync response using batched pagination.
    ///
    /// Sync flow:
    /// 1. Forward request to Kobo store with the original (unwrapped) sync token.
    /// 2. If Kobo is still paginating (x-kobo-sync: continue), pass through without merging — mixing
    ///    partial Kobo results with local books confuses the device's sync state machine.
    /// 3. Otherwise, check for local books with metadata or reading-state changes since last sync.
    ///    If none, return store results + collections.
    /// 4. If local books need syncing, start a batched session (keyed by device ID, 5-minute timeout)
    ///    and drain it across multiple requests using configurable batch size.
    ///
    /// Each batched book is classified into a <see cref="BookSyncAction"/> and dispatched to the
    /// matching flow method — the enum members and flow methods carry the per-case protocol rules.
    ///
    /// Collections are only sent in the final batch — sending earlier creates empty shelves before books arrive.
    /// </summary>
    public async Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request)
    {
        logger.LogInformation("LibrarySync: fetching store results and merging local books");
        var deviceKey = request.DeviceKey!;
        await koboDeviceRepository.UpdateLastSynced(Guid.Parse(deviceKey));
        var deviceBaseUrl = $"{alexandriaSettings.KoboApiUrl}/{deviceKey}";
        var alexandriaToken = koboDeviceSyncTokenService.ParseToken(GetSyncTokenHeader(request));

        request.Headers["x-kobo-synctoken"] = [alexandriaToken.KoboToken];
        var koboResponse = await koboProxyService.Proxy(request);

        var koboResponseToken = GetResponseSyncToken(koboResponse) ?? alexandriaToken.KoboToken;

        if (HasKoboContinuation(koboResponse))
        {
            koboDeviceSyncTokenService.Clear(deviceKey);
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
                await SyncStoreCollectionsToAlexandria(storeResults);
            }
        }

        var allBooks = await bookService.GetBooks();
        var syncedDates = await koboDeviceSyncRepository.GetSyncedModifiedDates(deviceKey);

        if (!koboDeviceSyncTokenService.IsActive(deviceKey))
        {
            var lastSync = alexandriaToken.LastSync;
            var books = allBooks.Where(b => b.ModifiedAt > lastSync || !syncedDates.ContainsKey(b.BookUuid) || b.ReadingProgress?.ModifiedAt > lastSync).ToList();
            if (books.Count == 0)
            {
                var collections = await GetCollectionSyncItems(lastSync, allBooks);
                syncResults.AddRange(collections);
                logger.LogInformation("LibrarySync: sending {Store} store items + {Collections} collections, no new local books since {LastSync}", storeCount, collections.Count, lastSync);
                var headers = new Dictionary<string, string[]>(koboResponse.Headers);
                headers.Remove("x-kobo-sync");
                headers["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
                return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, syncResults, headers);
            }

            logger.LogInformation("LibrarySync: syncing {Count} local books since {LastSync}", books.Count, lastSync);
            koboDeviceSyncTokenService.Start(deviceKey, books.Select(b => b.BookUuid).ToList());
        }

        var workGroupIds = BuildWorkGroupLookup(allBooks);

        var appSettings = await settingsService.Get();
        var batchSize = appSettings.SyncBatchSize;
        var batchUuids = koboDeviceSyncTokenService.GetNextBatch(deviceKey, batchSize);
        var hasMore = koboDeviceSyncTokenService.HasMore(deviceKey);

        foreach (var uuid in batchUuids)
        {
            var book = await bookService.GetBookByUuid(uuid);
            if (book is null)
                continue;

            var previouslySynced = syncedDates.TryGetValue(book.BookUuid, out var storedModifiedAt);
            var readingState = await GetDeviceReadingState(book);

            List<KoboStoreSyncResultItem> items = Classify(book, previouslySynced, storedModifiedAt, alexandriaToken.LastSync) switch
            {
                BookSyncAction.AddToDevice => await AddBookToDevice(deviceKey, book, deviceBaseUrl, readingState, workGroupIds),
                BookSyncAction.RemoveFromDevice => await RemoveBookFromDevice(deviceKey, book, deviceBaseUrl),
                BookSyncAction.ReplaceOnDevice => await ReplaceBookOnDevice(deviceKey, book, deviceBaseUrl, readingState, workGroupIds),
                BookSyncAction.ReadingStateOnly => SyncReadingStateToDevice(readingState),
                BookSyncAction.ConfirmOnDevice => ConfirmBookOnDevice(book, deviceBaseUrl, readingState, workGroupIds),
                _ => []
            };
            syncResults.AddRange(items);
        }

        var responseHeaders = new Dictionary<string, string[]>(koboResponse.Headers);

        if (hasMore)
        {
            responseHeaders["x-kobo-sync"] = ["continue"];
            responseHeaders["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = alexandriaToken.LastSync })];
            logger.LogInformation("LibrarySync: sending {Total} items ({Local} local, {Store} store), more local books pending", syncResults.Count, batchUuids.Count, storeCount);
        }
        else
        {
            var lastSyncTime = alexandriaToken.LastSync;
            var collections = await GetCollectionSyncItems(lastSyncTime, allBooks);
            syncResults.AddRange(collections);
            responseHeaders.Remove("x-kobo-sync");
            responseHeaders["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
            koboDeviceSyncTokenService.Clear(deviceKey);
            logger.LogInformation("LibrarySync: sending final {Total} items ({Local} local, {Store} store, {Collections} collections)", syncResults.Count, batchUuids.Count, storeCount, collections.Count);
        }

        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, syncResults, responseHeaders);
    }

    /// <summary>
    /// Returns metadata for a local book, or proxies to Kobo for store-purchased books.
    /// Includes RelatedGroupId when the same work exists as both ebook and audiobook.
    /// </summary>
    public async Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid)
    {
        var book = await bookService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("Metadata: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }
        var allBooks = await bookService.GetBooks();
        var relatedGroupId = GetRelatedGroupId(book, BuildWorkGroupLookup(allBooks));
        var deviceBaseUrl = $"{alexandriaSettings.KoboApiUrl}/{request.DeviceKey}";

        logger.LogInformation("Metadata: serving metadata for '{Title}' ({Uuid})", book.Title, uuid);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, new[] { KoboStoreEntityMapper.ToMetadata(book, deviceBaseUrl, relatedGroupId) });
    }

    /// <summary>
    /// Archives a local book (sets IsArchived=true) instead of deleting it — preserves metadata,
    /// reading state, and allows un-deletion. Returns 204 No Content. Store books are proxied to Kobo.
    /// </summary>
    public async Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid)
    {
        var book = await bookService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("DeleteBook: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        logger.LogInformation("DeleteBook: archiving '{Title}' ({Uuid})", book.Title, uuid);
        await bookService.SetBookArchiveStatus(book.BookUuid, true);

        return new KoboProxyResponse { StatusCode = StatusCodes.Status204NoContent, Headers = new Dictionary<string, string[]>(), Body = [] };
    }

    private static string? GetSyncTokenHeader(KoboProxyRequest request) => 
        request.Headers.TryGetValue("x-kobo-synctoken", out var values) && values.Length > 0 ? values[0] : null;

    private static string? GetResponseSyncToken(KoboProxyResponse response) => 
        response.Headers.TryGetValue("x-kobo-synctoken", out var values) && values.Length > 0 ? values[0] : null;

    private static bool HasKoboContinuation(KoboProxyResponse response) => response.Headers.TryGetValue("x-kobo-sync", out var values) && values.Any(v => string.Equals(v, "continue", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Determines which sync flow a book enters for this device. Evaluation order matters:
    /// device presence first, then archival, then metadata changes, and only then reading-state-only
    /// changes — progress written by another device or an external integration such as AudioBookShelf,
    /// which bumps the reading state's ModifiedAt without touching the book's.
    /// </summary>
    private static BookSyncAction Classify(Book book, bool previouslySynced, DateTimeOffset storedModifiedAt, DateTimeOffset lastSync)
    {
        if (!previouslySynced)
            return book.IsArchived ? BookSyncAction.None : BookSyncAction.AddToDevice;
        if (book.IsArchived)
            return BookSyncAction.RemoveFromDevice;
        if (storedModifiedAt != book.ModifiedAt)
            return BookSyncAction.ReplaceOnDevice;
        if (book.ReadingProgress?.ModifiedAt > lastSync)
            return BookSyncAction.ReadingStateOnly;
        return BookSyncAction.ConfirmOnDevice;
    }

    /// <summary>Loads the book's reading state re-keyed to the EntitlementUuid the device knows the book by.</summary>
    private async Task<KoboStoreReadingState?> GetDeviceReadingState(Book book)
    {
        var readingState = await readingStateRepository.GetByBookUuid(book.BookUuid);
        return readingState is null ? null : readingState with { EntitlementId = book.EntitlementUuid.ToString() };
    }

    private async Task<List<KoboStoreSyncResultItem>> AddBookToDevice(string deviceKey, Book book, string deviceBaseUrl, KoboStoreReadingState? readingState, HashSet<Guid> workGroupIds)
    {
        var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, deviceBaseUrl, readingState, GetRelatedGroupId(book, workGroupIds));
        await koboDeviceSyncRepository.UpsertSyncedFile(deviceKey, book.BookUuid, book.ModifiedAt);
        return [new KoboStoreSyncResultItem { NewEntitlement = entitlement }];
    }

    /// <summary>The sync record is deleted (not kept) so a later un-archive re-enters the AddToDevice flow cleanly.</summary>
    private async Task<List<KoboStoreSyncResultItem>> RemoveBookFromDevice(string deviceKey, Book book, string deviceBaseUrl)
    {
        var entitlement = KoboStoreEntityMapper.WithRemoved(KoboStoreEntityMapper.ToStoreEntitlement(book, deviceBaseUrl));
        await koboDeviceSyncRepository.RemoveSyncedFile(deviceKey, book.BookUuid);
        return [new KoboStoreSyncResultItem { ChangedEntitlement = entitlement }];
    }

    /// <summary>
    /// Kobo ignores metadata updates on entitlements the device already holds, so a changed book is
    /// sent as Remove + New. The reading state rides along as a separate ChangedReadingState item
    /// because the device ignores it inside entitlements.
    /// </summary>
    private async Task<List<KoboStoreSyncResultItem>> ReplaceBookOnDevice(string deviceKey, Book book, string deviceBaseUrl, KoboStoreReadingState? readingState, HashSet<Guid> workGroupIds)
    {
        var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, deviceBaseUrl, relatedGroupId: GetRelatedGroupId(book, workGroupIds));
        var items = new List<KoboStoreSyncResultItem>
        {
            new() { ChangedEntitlement = KoboStoreEntityMapper.WithRemoved(entitlement) },
            new() { NewEntitlement = entitlement with { ReadingState = null } }
        };
        if (readingState is not null)
            items.Add(new KoboStoreSyncResultItem { ChangedReadingState = new KoboStoreChangedReadingState { ReadingState = readingState } });
        await koboDeviceSyncRepository.UpsertSyncedFile(deviceKey, book.BookUuid, book.ModifiedAt);
        return items;
    }

    /// <summary>
    /// Pushes progress made outside this device (another Kobo, AudioBookShelf, MarkAsFinished) without
    /// re-sending the entitlement. The device's sync record is left untouched — it tracks the metadata
    /// version the device holds, which hasn't changed.
    /// </summary>
    private static List<KoboStoreSyncResultItem> SyncReadingStateToDevice(KoboStoreReadingState? readingState)
    {
        if (readingState is null)
            return [];
        return [new KoboStoreSyncResultItem { ChangedReadingState = new KoboStoreChangedReadingState { ReadingState = readingState } }];
    }

    private static List<KoboStoreSyncResultItem> ConfirmBookOnDevice(Book book, string deviceBaseUrl, KoboStoreReadingState? readingState, HashSet<Guid> workGroupIds)
    {
        var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, deviceBaseUrl, relatedGroupId: GetRelatedGroupId(book, workGroupIds));
        var items = new List<KoboStoreSyncResultItem> { new() { ChangedEntitlement = entitlement } };
        if (readingState is not null)
            items.Add(new KoboStoreSyncResultItem { ChangedReadingState = new KoboStoreChangedReadingState { ReadingState = readingState } });
        return items;
    }

    /// <summary>
    /// Builds collection sync items for the Kobo device. Collections reference books by EntitlementUuid
    /// in the Kobo protocol, but Alexandria stores them by BookUuid — the entitlementLookup translates
    /// between these ID spaces. Falls back to the raw BookUuid if no entitlement mapping exists.
    /// </summary>
    private async Task<List<KoboStoreSyncResultItem>> GetCollectionSyncItems(DateTimeOffset lastSync, List<Book> allBooks)
    {
        var collections = lastSync == DateTimeOffset.MinValue ? await collectionService.GetCollections() : await collectionService.GetCollectionsModifiedSince(lastSync);
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

    private async Task SyncStoreCollectionsToAlexandria(List<KoboStoreSyncResultItem> storeResults)
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

            await collectionService.UpsertCollection(collection);
            logger.LogInformation("LibrarySync: persisting store collection '{Name}' ({Uuid}) with {Count} books", collection.Name, collection.CollectionUuid, collection.BookUuids.Count);
        }
    }

    /// <summary>
    /// Identifies works that exist in multiple media types (e.g., same title+authors as both ebook
    /// and audiobook). The device uses RelatedGroupId to show "switch format" navigation.
    /// Single-format works don't need a group ID.
    /// </summary>
    private static HashSet<Guid> BuildWorkGroupLookup(List<Book> books)
    {
        return books
            .GroupBy(b => b.GenerateWorkUuid())
            .Where(g => g.Select(b => b.File.MediaType).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
    }

    private static Guid? GetRelatedGroupId(Book book, HashSet<Guid> workGroupIds)
    {
        var workUuid = book.GenerateWorkUuid();
        return workGroupIds.Contains(workUuid) ? workUuid : null;
    }

}
