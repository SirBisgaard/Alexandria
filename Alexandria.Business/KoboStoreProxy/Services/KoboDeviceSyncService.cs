using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboDeviceSyncService(KoboProxySettings settings, IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, IAlexandriaSettingsService settingsService, IFileContentManager contentManager, IReadingStateRepository readingStateRepository, IKoboDeviceSyncRepository koboDeviceSyncRepository, IKoboDeviceSyncTokenService koboDeviceSyncTokenService, ILogger<KoboDeviceSyncService> logger) : IKoboDeviceSyncService
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

        resources["library_sync"] = $"{settings.KoboApiUrl}/v1/library/sync";
        resources["image_host"] = $"{settings.KoboApiUrl}";
        resources["image_url_template"] = $"{settings.KoboApiUrl}/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg";
        resources["image_url_quality_template"] = $"{settings.KoboApiUrl}/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg";

        var headers = new Dictionary<string, string[]> { { "x-kobo-apitoken", ["e30="] } };
        return JsonResponse(StatusCodes.Status200OK, json!, headers);
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

        var syncedHashes = await koboDeviceSyncRepository.GetSyncedFileHashes(deviceId);

        if (!koboDeviceSyncTokenService.IsActive(deviceId))
        {
            var lastSync = alexandriaToken.LastSync;
            var books = (await alexandriaLibraryService.GetBooks()).Where(b => b.ModifiedAt > lastSync || !syncedHashes.ContainsKey(b.BookUuid)).ToList();
            if (books.Count == 0)
            {
                var collections = await GetCollectionSyncItems(lastSync);
                syncResults.AddRange(collections);
                logger.LogInformation("LibrarySync: returning {Store} store items + {Collections} collections, no new/changed local books since {LastSync}", storeCount, collections.Count, lastSync);
                var headers = new Dictionary<string, string[]>(koboResponse.Headers);
                headers.Remove("x-kobo-sync");
                headers["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
                return JsonResponse(StatusCodes.Status200OK, syncResults, headers);
            }

            logger.LogInformation("LibrarySync: {Count} local books to sync since {LastSync}", books.Count, lastSync);
            koboDeviceSyncTokenService.Start(deviceId, books.Select(b => b.BookUuid).ToList());
        }

        var allBooks = await alexandriaLibraryService.GetBooks();
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
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, settings.KoboApiUrl, readingState, relatedGroupId);
                syncResults.Add(new KoboStoreSyncResultItem { NewEntitlement = entitlement });
                await koboDeviceSyncRepository.UpsertSyncedFile(deviceId, book.BookUuid, book.FileHash);
            }
            else if (book.IsArchived)
            {
                var entitlement = KoboStoreEntityMapper.WithRemoved(KoboStoreEntityMapper.ToStoreEntitlement(book, settings.KoboApiUrl));
                syncResults.Add(new KoboStoreSyncResultItem { ChangedEntitlement = entitlement });
                await koboDeviceSyncRepository.RemoveSyncedFile(deviceId, book.BookUuid);
            }
            else if (storedHash != book.FileHash)
            {
                var readingState = await readingStateRepository.GetByBookUuid(book.BookUuid);
                if (readingState is not null)
                    readingState = readingState with { EntitlementId = book.EntitlementUuid.ToString() };

                var relatedGroupId = GetRelatedGroupId(book, workGroupIds);
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, settings.KoboApiUrl, relatedGroupId: relatedGroupId);
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
                var entitlement = KoboStoreEntityMapper.ToStoreEntitlement(book, settings.KoboApiUrl, relatedGroupId: relatedGroupId);
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
            var collections = await GetCollectionSyncItems(lastSyncTime);
            syncResults.AddRange(collections);
            responseHeaders.Remove("x-kobo-sync");
            responseHeaders["x-kobo-synctoken"] = [koboDeviceSyncTokenService.GenerateToken(new KoboDeviceSyncToken { KoboToken = koboResponseToken, LastSync = DateTimeOffset.UtcNow })];
            koboDeviceSyncTokenService.Clear(deviceId);
            logger.LogInformation("LibrarySync: returning final {Total} items ({Local} local, {Store} store, {Collections} collections)", syncResults.Count, batchUuids.Count, storeCount, collections.Count);
        }

        return JsonResponse(StatusCodes.Status200OK, syncResults, responseHeaders);
    }

    public async Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("Download: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        var fileBytes = await contentManager.GetContentFile(book);
        if (fileBytes is null)
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };

        logger.LogInformation("Download: serving local book '{Title}' ({Uuid})", book.Title, uuid);
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = new Dictionary<string, string[]>
            {
                { "Content-Type", ["application/epub+zip"] },
                { "Content-Disposition", [$"attachment; filename*=UTF-8''{Uri.EscapeDataString(book.Title)}.kepub.epub"] }
            },
            Body = fileBytes
        };
    }

    public async Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var book = await alexandriaLibraryService.GetBookByCoverImageUuid(uuid) ?? await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("CoverImage: book {Uuid} not local, redirecting to Kobo CDN", uuid);
            return new KoboProxyResponse
            {
                StatusCode = StatusCodes.Status307TemporaryRedirect,
                Headers = new Dictionary<string, string[]> { { "Location", [$"https://cdn.kobo.com/book-images/{uuid}/{width}/{height}/false/image.jpg"] } },
                Body = []
            };
        }

        var qualityInt = int.TryParse(quality, out var q) ? q : 80;
        var coverBytes = await contentManager.GetCoverFile(book, width, height, qualityInt);
        if (coverBytes is null)
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };

        logger.LogInformation("CoverImage: serving cover for '{Title}' ({Uuid})", book.Title, uuid);
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = new Dictionary<string, string[]> { { "Content-Type", ["image/jpeg"] } },
            Body = coverBytes
        };
    }

    public async Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("ReadingState: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }
        logger.LogInformation("ReadingState: {Method} for '{Title}' ({Uuid})", request.Method, book.Title, uuid);

        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entitlementUuidStr = book.EntitlementUuid.ToString();

        if (request.Method == "PUT")
            return await HandleReadingStatePut(request, book, entitlementUuidStr, now);

        var storedState = await readingStateRepository.GetByBookUuid(book.BookUuid);
        if (storedState is not null)
        {
            storedState = storedState with { EntitlementId = entitlementUuidStr };
            return JsonResponse(StatusCodes.Status200OK, new[] { storedState });
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

        return JsonResponse(StatusCodes.Status200OK, new[] { defaultState });
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
        return JsonResponse(StatusCodes.Status200OK, new[] { KoboStoreEntityMapper.ToMetadata(book, settings.KoboApiUrl, relatedGroupId) });
    }

    public async Task<KoboProxyResponse> DownloadManifest(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("DownloadManifest: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        if (book.MediaType != MediaType.Audiobook)
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };

        logger.LogInformation("DownloadManifest: serving manifest for '{Title}' ({Uuid})", book.Title, uuid);
        var manifest = KoboStoreEntityMapper.ToManifest(book, settings.KoboApiUrl);
        return JsonResponse(StatusCodes.Status200OK, manifest);
    }

    public async Task<KoboProxyResponse> AudiobookChapter(KoboProxyRequest request, Guid uuid, int partId)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogDebug("AudiobookChapter: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        var chapterBytes = await contentManager.GetChapterFile(book, partId);
        if (chapterBytes is null)
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };

        logger.LogInformation("AudiobookChapter: serving chapter {PartId} of '{Title}' ({Uuid})", partId, book.Title, uuid);
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = new Dictionary<string, string[]>
            {
                { "Content-Type", ["audio/mpeg"] }
            },
            Body = chapterBytes
        };
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

    public async Task<KoboProxyResponse> Series(KoboProxyRequest request, Guid uuid)
    {
        var series = await alexandriaLibraryService.GetSeriesByUuid(uuid);
        if (series is null)
            return await koboProxyService.Proxy(request);

        logger.LogDebug("Series: returning {Count} books for local series '{Name}' ({Uuid})", series.Books.Count, series.Name, uuid);

        var items = series.Books.Select(b => b.MediaType == MediaType.Audiobook
            ? new KoboStoreProductRecommendationItem { Audiobook = MapToAudiobookProduct(b) }
            : new KoboStoreProductRecommendationItem { Book = MapToEbookProduct(b) }).ToList();

        return JsonResponse(StatusCodes.Status200OK, new KoboStoreProductRecommendations
        {
            Items = items,
            ItemCount = items.Count,
            TotalItemCount = items.Count,
            TotalPageCount = items.Count > 0 ? 1 : 0
        });
    }

    public Task<KoboProxyResponse> Recommendations(KoboProxyRequest request)
    {
        logger.LogDebug("Recommendations: returning empty stub");
        return Task.FromResult(JsonResponse(StatusCodes.Status200OK, new KoboStoreProductRecommendations { ItemsPerPage = 50 }));
    }

    public async Task<KoboProxyResponse> NextRead(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        logger.LogDebug("NextRead: returning empty stub for local book '{Title}' ({Uuid})", book.Title, uuid);
        return JsonResponse(StatusCodes.Status200OK, new object());
    }

    public async Task<KoboProxyResponse> ProductRecommendations(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        var recommendations = await alexandriaLibraryService.GetRecommendations(book.BookUuid);
        logger.LogDebug("ProductRecommendations: returning {Count} recommendations for '{Title}' ({Uuid})", recommendations.Count, book.Title, uuid);

        var items = recommendations.Select(r => book.MediaType == MediaType.Audiobook
            ? new KoboStoreProductRecommendationItem { Audiobook = MapToAudiobookProduct(r) }
            : new KoboStoreProductRecommendationItem { Book = MapToEbookProduct(r) }).ToList();

        var response = new KoboStoreProductRecommendations
        {
            Items = items,
            ItemCount = items.Count,
            TotalItemCount = items.Count,
            TotalPageCount = items.Count > 0 ? 1 : 0
        };

        return JsonResponse(StatusCodes.Status200OK, response);
    }

    public async Task<KoboProxyResponse> AudiobookProduct(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        logger.LogDebug("AudiobookProduct: returning local product for '{Title}' ({Uuid})", book.Title, uuid);
        return JsonResponse(StatusCodes.Status200OK, MapToAudiobookProduct(book));
    }

    public async Task<KoboProxyResponse> EbookProduct(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
        if (book is null)
            return await koboProxyService.Proxy(request);

        logger.LogDebug("EbookProduct: returning local product for '{Title}' ({Uuid})", book.Title, uuid);
        return JsonResponse(StatusCodes.Status200OK, MapToEbookProduct(book));
    }

    private static KoboStoreProductRecommendationAudiobook MapToAudiobookProduct(Book book)
    {
        return new KoboStoreProductRecommendationAudiobook
        {
            Id = book.EntitlementUuid.ToString(),
            Title = book.Title,
            Contributors = string.Join(",", book.Authors.Select(a => a.Name)),
            ContributorRoles = book.Authors.Select(a => new KoboStoreContributorRole { Name = a.Name }).ToList(),
            Description = book.Description,
            Language = book.Language,
            Duration = book.Duration ?? 0,
            TotalBytes = book.TotalFileSize,
            IsAbridged = book.IsAbridged ?? false,
            ISBN = book.Isbn,
            PublicationDate = DateTimeOffset.TryParse(book.PublicationDate, out var pubDate) ? pubDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") : book.PublicationDate,
            Subtitle = book.Subtitle,
            PublisherName = book.Publisher,
            ImageId = book.CoverImageUuid?.ToString(),
            SeriesName = book.Series?.Name,
            SeriesNumber = book.Series?.Index is not null ? ((int)book.Series.Index.Value).ToString() : null,
            SeriesId = book.Series?.SeriesUuid.ToString(),
            SeriesNumberFloat = book.Series?.Index is not null ? (float)book.Series.Index.Value : null
        };
    }

    private static KoboStoreEbookProduct MapToEbookProduct(Book book)
    {
        return new KoboStoreEbookProduct
        {
            Id = book.EntitlementUuid.ToString(),
            Title = book.Title,
            Contributors = string.Join(",", book.Authors.Select(a => a.Name)),
            ContributorRoles = book.Authors.Select(a => new KoboStoreContributorRole { Name = a.Name }).ToList(),
            Description = book.Description,
            Language = book.Language,
            Locale = new KoboStoreLocale { LanguageCode = book.Language },
            ISBN = book.Isbn,
            PublicationDate = DateTimeOffset.TryParse(book.PublicationDate, out var pubDate) ? pubDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") : book.PublicationDate,
            Subtitle = book.Subtitle,
            PublisherName = book.Publisher,
            ImageId = book.CoverImageUuid?.ToString(),
            SeriesName = book.Series?.Name,
            SeriesNumber = book.Series?.Index is not null ? ((int)book.Series.Index.Value).ToString() : null,
            SeriesId = book.Series?.SeriesUuid.ToString(),
            SeriesNumberFloat = book.Series?.Index is not null ? (float)book.Series.Index.Value : null,
            Stats = book.PageCount is not null || book.WordCount is not null ? new KoboStoreEbookStats { Pages = book.PageCount ?? 0, WordCount = book.WordCount ?? 0 } : null
        };
    }

    public async Task<KoboProxyResponse> ProductPrices(KoboProxyRequest request, string uuids)
    {
        var uuidList = uuids.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var priceItems = new List<KoboStoreProductPriceItem>();
        var hasLocal = false;

        foreach (var id in uuidList)
        {
            if (!Guid.TryParse(id.Trim(), out var uuid))
                continue;

            var book = await alexandriaLibraryService.GetBookByEntitlementUuid(uuid);
            if (book is null)
                continue;

            hasLocal = true;
            priceItems.Add(new KoboStoreProductPriceItem
            {
                Id = book.EntitlementUuid.ToString(),
                Price = new KoboStorePrice { Currency = "USD", Price = 0 }
            });
        }

        if (!hasLocal)
            return await koboProxyService.Proxy(request);

        logger.LogDebug("ProductPrices: returning {Count} local price items", priceItems.Count);
        return JsonResponse(StatusCodes.Status200OK, new KoboStoreProductPrices { Items = priceItems });
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

    private async Task<List<KoboStoreSyncResultItem>> GetCollectionSyncItems(DateTimeOffset lastSync)
    {
        var collections = lastSync == DateTimeOffset.MinValue ? await alexandriaLibraryService.GetCollections() : await alexandriaLibraryService.GetCollectionsModifiedSince(lastSync);
        var allBooks = await alexandriaLibraryService.GetBooks();
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
                return JsonResponse(StatusCodes.Status200OK, putResponse);
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

        return JsonResponse(StatusCodes.Status200OK, putResponse);
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

    private static KoboProxyResponse JsonResponse(int statusCode, object body, Dictionary<string, string[]>? extraHeaders = null)
    {
        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        var headers = extraHeaders ?? new Dictionary<string, string[]>();
        headers["Content-Type"] = ["application/json; charset=utf-8"];
        return new KoboProxyResponse { StatusCode = statusCode, Headers = headers, Body = bytes };
    }
}
