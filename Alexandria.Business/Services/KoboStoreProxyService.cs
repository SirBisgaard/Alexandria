using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;
using Alexandria.Business.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

/// <summary>
/// Man-in-the-middle proxy between Kobo e-readers and storeapi.kobo.com.
/// Intercepts specific Kobo API endpoints to inject local Alexandria library books into the sync flow.
/// Unrecognized requests are transparently forwarded to Kobo via <see cref="Proxy"/>.
/// </summary>
public class KoboStoreProxyService(KoboProxySettings settings, HttpClient httpClient, IAlexandriaLibraryService alexandriaLibraryService, IFileRepository fileRepository, IReadingStateRepository readingStateRepository, SyncSessionService syncSession, ILogger<KoboStoreProxyService> logger) : IKoboProxyService
{
    // Headers that shouldn't be forwarded in either direction
    private static readonly HashSet<string> HopByHopHeaders =
    [
        "connection", "keep-alive", "transfer-encoding",
        "te", "trailer", "upgrade", "host"
    ];

    /// <summary>
    /// Intercepts Kobo's /v1/initialization endpoint. Proxies the request to Kobo, then rewrites the
    /// Resources URLs in the response so the device routes library_sync and image requests back through Alexandria.
    /// Also overrides the API token header with a static value to keep the device happy.
    /// </summary>
    public async Task<KoboProxyResponse> Initialization(KoboProxyRequest request)
    {
        logger.LogInformation("Initialization: rewriting resource URLs to route through Alexandria");
        var koboResponse = await Proxy(request);

        var json = JsonSerializer.Deserialize<JsonObject>(koboResponse.Body);
        var resources = json?["Resources"]?.AsObject();

        if (resources is null)
            return koboResponse;

        // Override only the endpoints we need to intercept
        resources["library_sync"] = $"{settings.AlexandriaApi}/v1/library/sync";
        resources["image_host"] = $"{settings.AlexandriaApi}";
        resources["image_url_template"] = $"{settings.AlexandriaApi}/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg";
        resources["image_url_quality_template"] = $"{settings.AlexandriaApi}/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg";


        var headers = new Dictionary<string, string[]> { { "x-kobo-apitoken", ["e30="] } };
        return JsonResponse(StatusCodes.Status200OK, json!, headers);
    }

    /// <summary>
    /// Intercepts Kobo's /v1/library/sync endpoint. Uses a two-phase continuation model:
    /// Phase 1 — while Kobo returns x-kobo-sync: continue, pass through store results without injecting local books.
    /// Phase 2 — once Kobo is done, paginate local Alexandria books in batches using the same continuation header.
    /// </summary>
    public async Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request)
    {
        logger.LogInformation("LibrarySync: fetching store results and merging local books");
        var deviceId = GetDeviceId(request);
        var koboResponse = await Proxy(request);

        // If Kobo is still paginating its own results, pass through without injecting local books.
        if (HasKoboContinuation(koboResponse))
        {
            syncSession.Clear(deviceId);
            logger.LogInformation("LibrarySync: Kobo continuation active, passing through store results");
            return koboResponse;
        }

        var syncResults = new List<SyncResultItem>();
        var storeCount = 0;

        if (koboResponse.StatusCode == 200)
        {
            var storeResults = JsonSerializer.Deserialize<List<SyncResultItem>>(koboResponse.Body);
            if (storeResults is not null)
            {
                storeCount = storeResults.Count;
                syncResults.AddRange(storeResults);
            }
        }

        // Start a new local book sync session if one isn't already active.
        if (!syncSession.IsActive(deviceId))
        {
            var books = (await alexandriaLibraryService.GetBooks()).Where(b => !b.IsArchived).ToList();
            if (books.Count == 0)
            {
                logger.LogInformation("LibrarySync: returning {Store} store items, no local books", storeCount);
                return JsonResponse(StatusCodes.Status200OK, syncResults, koboResponse.Headers);
            }

            syncSession.Start(deviceId, books.Select(b => b.Uuid).ToList());
        }

        // Get next batch of local book UUIDs from the session.
        var batchSize = settings.SyncBatchSize;
        var batchUuids = syncSession.GetNextBatch(deviceId, batchSize);
        var hasMore = syncSession.HasMore(deviceId);

        foreach (var uuid in batchUuids)
        {
            var book = await alexandriaLibraryService.GetBookByUuid(uuid);
            if (book is null || book.IsArchived)
            {
                continue;
            }

            var readingState = await readingStateRepository.GetByBookUuid(book.Uuid);
            syncResults.Add(new SyncResultItem
            {
                NewEntitlement = new Entitlement
                {
                    BookEntitlement = book.ToEntitlement(),
                    BookMetadata = book.ToMetadata(settings.AlexandriaApi),
                    ReadingState = readingState
                }
            });
        }

        var headers = new Dictionary<string, string[]>(koboResponse.Headers);

        if (hasMore)
        {
            headers["x-kobo-sync"] = ["continue"];
            logger.LogInformation("LibrarySync: returning {Total} items ({Local} local, {Store} store), more local books pending", syncResults.Count, batchUuids.Count, storeCount);
        }
        else
        {
            headers.Remove("x-kobo-sync");
            syncSession.Clear(deviceId);
            logger.LogInformation("LibrarySync: returning final {Total} items ({Local} local, {Store} store)", syncResults.Count, batchUuids.Count, storeCount);
        }

        return JsonResponse(StatusCodes.Status200OK, syncResults, headers);
    }

    private static string GetDeviceId(KoboProxyRequest request)
    {
        if (request.Headers.TryGetValue("x-kobo-deviceid", out var values) && values.Length > 0 && values[0] is not null)
        {
            return values[0]!;
        }

        return "unknown";
    }

    private static bool HasKoboContinuation(KoboProxyResponse response)
    {
        return response.Headers.TryGetValue("x-kobo-sync", out var values) && values.Any(v => string.Equals(v, "continue", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Serves the KEPUB file for a local book when the Kobo device requests a download.
    /// Returns the file as application/epub+zip with a Content-Disposition attachment header.
    /// If the book is not in Alexandria's library, proxies to Kobo for store-purchased downloads.
    /// </summary>
    public async Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        // We dont know the book.
        if (book is null)
        {
            logger.LogDebug("Download: book {Uuid} not local, proxying to Kobo", uuid);
            return await Proxy(request);
        }

        // If the book does not exist on disk return 404.
        var fileBytes = await fileRepository.ReadKepub(book);
        if (fileBytes is null)
        {
            return new KoboProxyResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Headers = new Dictionary<string, string[]>(),
                Body = []
            };
        }

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

    /// <summary>
    /// Serves cover images for local books. The Kobo device requests covers via the image_url_template
    /// that was rewritten during initialization. For unknown books, issues a 307 redirect to Kobo's CDN
    /// at cdn.kobo.com so the device fetches store covers directly. Returns 404 if the book is local but has no cover.
    /// </summary>
    public async Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        // We dont know the book.
        if (book is null)
        {
            logger.LogDebug("CoverImage: book {Uuid} not local, redirecting to Kobo CDN", uuid);
            return new KoboProxyResponse
            {
                StatusCode = StatusCodes.Status307TemporaryRedirect,
                Headers = new Dictionary<string, string[]>
                {
                    { "Location", [$"https://cdn.kobo.com/book-images/{uuid}/{width}/{height}/false/image.jpg"] }
                },
                Body = []
            };
        }

        // If the book does not have a cover return 404.
        var coverBytes = await fileRepository.ReadCover(book);
        if (coverBytes is null)
        {
            return new KoboProxyResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Headers = new Dictionary<string, string[]>(),
                Body = []
            };
        }

        // Return the cover.
        logger.LogInformation("CoverImage: serving cover for '{Title}' ({Uuid})", book.Title, uuid);
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = new Dictionary<string, string[]>
            {
                { "Content-Type", ["image/jpeg"] }
            },
            Body = coverBytes
        };
    }

    /// <summary>
    /// Handles reading state for local books (GET and PUT on /v1/library/{uuid}/state).
    /// GET returns persisted reading state, or a default "ReadyToRead" if none exists.
    /// PUT persists the device's progress (bookmark, statistics, status) and returns a success acknowledgement.
    /// For store-purchased books, proxies to Kobo.
    /// </summary>
    public async Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        // We dont know the book.
        if (book is null)
        {
            logger.LogDebug("ReadingState: book {Uuid} not local, proxying to Kobo", uuid);
            return await Proxy(request);
        }
        logger.LogInformation("ReadingState: {Method} for '{Title}' ({Uuid})", request.Method, book.Title, uuid);

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var uuidStr = uuid.ToString();

        if (request.Method == "PUT")
        {
            return await HandleReadingStatePut(request, uuid, uuidStr, now, book);
        }

        // GET — return persisted reading state, or default.
        var storedState = await readingStateRepository.GetByBookUuid(uuid);
        if (storedState is not null)
        {
            return JsonResponse(StatusCodes.Status200OK, new[] { storedState });
        }

        var defaultState = new ReadingState
        {
            EntitlementId = uuidStr,
            Created = book.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new StatusInfo { LastModified = now, Status = "ReadyToRead" },
            Statistics = new Statistics { LastModified = now },
            CurrentBookmark = new Bookmark { LastModified = now }
        };

        return JsonResponse(StatusCodes.Status200OK, new[] { defaultState });
    }

    /// <summary>
    /// Returns metadata for a local book (title, authors, download URLs, cover image ID, series info, etc.)
    /// as a single-element JSON array matching the Kobo /v1/library/{uuid}/metadata response format.
    /// For store-purchased books, proxies to Kobo.
    /// </summary>
    public async Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        // We dont know the book.
        if (book is null)
        {
            logger.LogDebug("Metadata: book {Uuid} not local, proxying to Kobo", uuid);
            return await Proxy(request);
        }
        logger.LogInformation("Metadata: serving metadata for '{Title}' ({Uuid})", book.Title, uuid);
        
        return JsonResponse(StatusCodes.Status200OK, new[] { book.ToMetadata(settings.AlexandriaApi) });
    }

    /// <summary>
    /// Handles book deletion from the Kobo device (DELETE /v1/library/{uuid}).
    /// For local books, marks the book as archived in the index so it won't reappear on future syncs.
    /// The actual EPUB/KEPUB files are not deleted. Returns 204 No Content. For store books, proxies to Kobo.
    /// </summary>
    public async Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        // We dont know the book.
        if (book is null)
        {
            logger.LogDebug("DeleteBook: book {Uuid} not local, proxying to Kobo", uuid);
            return await Proxy(request);
        }

        logger.LogInformation("DeleteBook: archiving '{Title}' ({Uuid})", book.Title, uuid);
        await alexandriaLibraryService.ArchiveBook(uuid);
        
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status204NoContent,
            Headers = new Dictionary<string, string[]>(),
            Body = []
        };

    }

    /// <summary>
    /// Transparently forwards a request to storeapi.kobo.com and returns the raw response.
    /// Used as the fallback for store-purchased books and as the catch-all for unintercepted endpoints.
    /// Copies all headers except hop-by-hop headers, routing content headers to the request content.
    /// </summary>
    public async Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
    {
        var targetUrl = $"{settings.StoreApi}{request.Path}{request.QueryString}";
        logger.LogInformation("Proxy: {Method} {Url}", request.Method, targetUrl);
        var upstream = new HttpRequestMessage(new HttpMethod(request.Method), targetUrl);

        // Copy body
        if (request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(request.Body);

        // Copy headers
        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key.ToLowerInvariant()))
                continue;

            // Content headers go on the content, not the request
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                && upstream.Content is not null)
            {
                upstream.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            else
            {
                upstream.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Send to Kobo
        var response = await httpClient.SendAsync(upstream);

        // Read and decompress the response if gzip-encoded
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        var contentEncoding = response.Content.Headers.ContentEncoding.FirstOrDefault();

        if (string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
        {
            using var compressed = new MemoryStream(responseBody);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            await gzip.CopyToAsync(decompressed);
            responseBody = decompressed.ToArray();
        }

        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .Where(h => !h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToArray());

        return new KoboProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = responseHeaders,
            Body = responseBody
        };
    }
    
    private async Task<KoboProxyResponse> HandleReadingStatePut(KoboProxyRequest request, Guid uuid, string uuidStr, string now, Book book)
    {
        var rawBody = Encoding.UTF8.GetString(request.Body);
        var putRequest = JsonSerializer.Deserialize<ReadingStatePutRequest>(rawBody);
        var item = putRequest?.ReadingStates?.FirstOrDefault();

        if (item is not null)
        {
            // Load existing state to handle status transitions.
            var existing = await readingStateRepository.GetByBookUuid(uuid);
            var timesStarted = existing?.StatusInfo.TimesStartedReading ?? 0;
            string? lastTimeStarted = existing?.StatusInfo.LastTimeStartedReading;
            var newStatus = item.StatusInfo?.Status ?? existing?.StatusInfo.Status ?? "ReadyToRead";

            // Increment times started reading when transitioning to "Reading".
            if (newStatus == "Reading" && (existing is null || existing.StatusInfo.Status != "Reading"))
            {
                timesStarted++;
                lastTimeStarted = now;
            }

            var state = new ReadingState
            {
                EntitlementId = uuidStr,
                Created = existing?.Created ?? book.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                LastModified = now,
                PriorityTimestamp = now,
                StatusInfo = new StatusInfo
                {
                    LastModified = now,
                    Status = newStatus,
                    TimesStartedReading = timesStarted,
                    LastTimeStartedReading = lastTimeStarted
                },
                Statistics = new Statistics
                {
                    LastModified = now,
                    SpentReadingMinutes = item.Statistics?.SpentReadingMinutes ?? existing?.Statistics.SpentReadingMinutes,
                    RemainingTimeMinutes = item.Statistics?.RemainingTimeMinutes ?? existing?.Statistics.RemainingTimeMinutes
                },
                CurrentBookmark = new Bookmark
                {
                    LastModified = now,
                    ProgressPercent = item.CurrentBookmark?.ProgressPercent ?? existing?.CurrentBookmark.ProgressPercent,
                    ContentSourceProgressPercent = item.CurrentBookmark?.ContentSourceProgressPercent ?? existing?.CurrentBookmark.ContentSourceProgressPercent,
                    Location = item.CurrentBookmark?.Location ?? existing?.CurrentBookmark.Location
                }
            };

            await readingStateRepository.Upsert(uuid, state, rawBody);
        }

        var putResponse = new
        {
            RequestResult = "Success",
            UpdateResults = new[]
            {
                new
                {
                    EntitlementId = uuidStr,
                    CurrentBookmarkResult = new { Result = "Success" },
                    StatisticsResult = new { Result = "Success" },
                    StatusInfoResult = new { Result = "Success" },
                    LastModified = now,
                    PriorityTimestamp = now
                }
            }
        };

        return JsonResponse(StatusCodes.Status200OK, putResponse);
    }

    private static KoboProxyResponse JsonResponse(int statusCode, object body, Dictionary<string, string[]>? extraHeaders = null)
    {
        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        var headers = extraHeaders ?? new Dictionary<string, string[]>();
        headers["Content-Type"] = ["application/json; charset=utf-8"];
        return new KoboProxyResponse
        {
            StatusCode = statusCode,
            Headers = headers,
            Body = bytes
        };
    }
}
