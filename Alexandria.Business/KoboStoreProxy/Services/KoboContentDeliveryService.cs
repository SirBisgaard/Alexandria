using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboContentDeliveryService(AlexandriaSettings alexandriaSettings, IKoboProxyService koboProxyService, IBookService bookService, IFileContentManager contentManager, ILogger<KoboContentDeliveryService> logger) : IKoboContentDeliveryService
{
    /// <summary>
    /// Serves KEPUB file for local books, proxies to Kobo for store books.
    /// Content-Disposition uses ".kepub.epub" extension — Kobo devices reject downloads without it.
    /// UTF-8 filename encoding (RFC 5987) handles non-ASCII titles.
    /// </summary>
    public async Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid)
    {
        var book = await bookService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("Download: book {Uuid} not local, proxying to Kobo", uuid);
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

    /// <summary>
    /// Serves cover images for local books. Unknown books get a 307 redirect to Kobo's CDN.
    /// 307 (not 302) preserves the HTTP method — Kobo devices make HEAD requests for cover
    /// probing and 302 would downgrade them to GET.
    /// </summary>
    public async Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var book = await bookService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("CoverImage: book {Uuid} not local, redirecting to Kobo CDN", uuid);
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

    /// <summary>
    /// Serves the audiobook manifest JSON for local audiobooks. Audiobook downloads are two-step:
    /// the device fetches this manifest first, then streams individual chapters on demand via
    /// the AudiobookChapter endpoint. Store audiobooks are proxied to Kobo.
    /// </summary>
    public async Task<KoboProxyResponse> DownloadManifest(KoboProxyRequest request, Guid uuid)
    {
        var book = await bookService.GetBookByEntitlementUuid(uuid);
        if (book is null)
        {
            logger.LogInformation("DownloadManifest: book {Uuid} not local, proxying to Kobo", uuid);
            return await koboProxyService.Proxy(request);
        }

        if (book.File.MediaType != MediaType.Audiobook)
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };

        logger.LogInformation("DownloadManifest: serving manifest for '{Title}' ({Uuid})", book.Title, uuid);
        var deviceBaseUrl = $"{alexandriaSettings.KoboApiUrl}/{request.DeviceKey}";
        var manifest = KoboStoreEntityMapper.ToManifest(book, deviceBaseUrl);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, manifest);
    }

    public async Task<Book?> ResolveLocalAudiobook(Guid uuid)
    {
        return await bookService.GetBookByEntitlementUuid(uuid);
    }

    public async Task StreamAudiobookChapter(Book book, int partId, Stream outputStream, CancellationToken cancellationToken)
    {
        if (partId < 0 || partId >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(partId));

        logger.LogInformation("AudiobookChapter: streaming chapter {PartId} of '{Title}' ({Uuid})", partId, book.Title, book.EntitlementUuid);
        await contentManager.StreamChapter(book, partId, outputStream, cancellationToken);
    }
}
