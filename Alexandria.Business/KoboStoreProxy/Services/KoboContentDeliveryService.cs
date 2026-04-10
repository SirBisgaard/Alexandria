using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboContentDeliveryService(AlexandriaSettings alexandriaSettings, IKoboProxyService koboProxyService, IAlexandriaLibraryService alexandriaLibraryService, IFileContentManager contentManager, ILogger<KoboContentDeliveryService> logger) : IKoboContentDeliveryService
{
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
        var manifest = KoboStoreEntityMapper.ToManifest(book, alexandriaSettings.KoboApiUrl);
        return KoboProxyResponseHelper.JsonResponse(StatusCodes.Status200OK, manifest);
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
}
