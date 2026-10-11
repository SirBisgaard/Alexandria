using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboContentDeliveryService(AlexandriaSettings alexandriaSettings, IKoboProxyService koboProxyService, IBookService bookService, IFileContentManager contentManager, IKoboDeviceRepository koboDeviceRepository, IComicRenditionStore comicRenditionStore, ILogger<KoboContentDeliveryService> logger) : IKoboContentDeliveryService
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

        if (book.File.MediaType == MediaType.Comic)
            return await DownloadComic(request, book);

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
    /// Serves the fixed-layout EPUB rendition built for the requesting device's screen profile.
    /// 404 when the device's model is unknown or the rendition hasn't been built — sync never
    /// advertises a comic in that state, so this only happens if the rendition was removed since.
    /// </summary>
    private async Task<KoboProxyResponse> DownloadComic(KoboProxyRequest request, Book book)
    {
        var device = await koboDeviceRepository.GetByUuid(Guid.Parse(request.DeviceKey!));
        var profile = ComicDeviceProfile.ForModelCode(device?.ModelCode);
        var rendition = profile is null ? null : await comicRenditionStore.FindRendition(book, profile);
        if (rendition is null)
        {
            logger.LogWarning("Download: no comic rendition of '{Title}' for device {DeviceKey}", book.Title, request.DeviceKey);
            return new KoboProxyResponse { StatusCode = StatusCodes.Status404NotFound, Headers = new Dictionary<string, string[]>(), Body = [] };
        }

        logger.LogInformation("Download: serving {Profile} comic rendition of '{Title}' ({Uuid})", profile!.Key, book.Title, book.EntitlementUuid);
        return new KoboProxyResponse
        {
            StatusCode = StatusCodes.Status200OK,
            Headers = new Dictionary<string, string[]>
            {
                { "Content-Type", ["application/epub+zip"] },
                { "Content-Disposition", [$"attachment; filename*=UTF-8''{Uri.EscapeDataString(book.Title)}.epub"] }
            },
            Body = await File.ReadAllBytesAsync(rendition.FullName)
        };
    }

    /// <summary>
    /// Serves cover images for local books. Unknown books get a 307 redirect to Kobo's CDN.
    /// 307 (not 302) preserves the HTTP method — Kobo devices make HEAD requests for cover
    /// probing and 302 would downgrade them to GET. The incoming UUID is the cache-busting
    /// cover UUID v5 (BookUuid + ModifiedAt); BookUuid / EntitlementUuid are tried as fallbacks
    /// so legacy URLs cached on the device keep resolving.
    /// </summary>
    public async Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var book = await ResolveBookForCover(uuid);
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

    /// <summary>
    /// Maps an incoming cover image UUID back to a book. The primary path scans a narrow
    /// (BookUuid, ModifiedAt) projection and recomputes the deterministic cover UUID
    /// (<see cref="BookExtensions.CoverImageUuid"/>) for each row — cheap enough to run per request.
    /// BookUuid and EntitlementUuid are tried as fallbacks so legacy URLs still resolve.
    /// </summary>
    private async Task<Book?> ResolveBookForCover(Guid uuid)
    {
        var keys = await bookService.GetCoverLookupKeys();
        var match = keys.FirstOrDefault(k => BookExtensions.CoverImageUuid(k.BookUuid, k.ModifiedAt) == uuid);
        if (match is not null)
            return await bookService.GetBookByUuid(match.BookUuid);

        return await bookService.GetBookByUuid(uuid) ?? await bookService.GetBookByEntitlementUuid(uuid);
    }

    public async Task StreamAudiobookChapter(Book book, int partId, Stream outputStream, CancellationToken cancellationToken)
    {
        if (partId < 0 || partId >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(partId));

        logger.LogInformation("AudiobookChapter: streaming chapter {PartId} of '{Title}' ({Uuid})", partId, book.Title, book.EntitlementUuid);
        await contentManager.StreamChapter(book, partId, outputStream, cancellationToken);
    }
}
