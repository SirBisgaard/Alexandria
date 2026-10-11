using Alexandria.Api.Authentication;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Kobo;

/// <summary>
/// Intercepts specific Kobo store API endpoints to inject local Alexandria library books.
/// Routes here take priority over <see cref="KoboController"/>'s catch-all.
/// The <c>{devicekey}</c> route segment identifies and authenticates the device.
/// </summary>
[ApiController]
[Route("/api/kobo/{devicekey}")]
[Authorize(AuthenticationSchemes = KoboDeviceAuthHandler.SchemeName)]
public class KoboDeviceController(IKoboLibrarySyncService librarySyncService, IKoboContentDeliveryService contentDeliveryService, IKoboReadingStateService readingStateService, IKoboProductService productService, IKoboAnalyticsService analyticsService, IKoboProxyService koboProxyService, ILogger<KoboDeviceController> logger) : ControllerBase
{
    /// <summary>
    /// Handles the device's auth handshake. The device is already pre-registered via the UI;
    /// serial binding and model detection are handled by <see cref="KoboDeviceAuthHandler"/>.
    /// We proxy the request to Kobo so the device receives a valid bearer token.
    /// </summary>
    [HttpPost("v1/auth/device")]
    public async Task AuthDevice(string devicekey)
    {
        logger.LogInformation("Kobo: auth/device for {DeviceKey}", devicekey);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await koboProxyService.Proxy(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Intercepts /v1/initialization to rewrite Kobo's resource URLs so the device
    /// routes sync, image, and download requests back through Alexandria.
    /// </summary>
    [HttpGet("v1/initialization")]
    public async Task Initialize(string devicekey)
    {
        logger.LogInformation("Kobo: initializing device");
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await librarySyncService.Initialization(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Intercepts /v1/library/sync to merge local Alexandria books into Kobo's sync response.
    /// The device discovers new books to download through this endpoint.
    /// </summary>
    [HttpGet("v1/library/sync")]
    public async Task LibrarySync(string devicekey)
    {
        logger.LogInformation("Kobo: syncing library");
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await librarySyncService.LibrarySync(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves KEPUB file downloads for local ebooks and fixed-layout EPUB renditions for local comics.
    /// Store-purchased book downloads are proxied to Kobo.
    /// </summary>
    [HttpGet("download/{uuid:guid}/kepub")]
    [HttpGet("download/{uuid:guid}/epub")]
    public async Task Download(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: downloading book {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await contentDeliveryService.Download(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves the audiobook manifest JSON for local audiobooks. Contains chapter list with download URLs.
    /// Store-purchased audiobook manifests are proxied to Kobo.
    /// </summary>
    [HttpGet("download/{uuid:guid}/manifest")]
    public async Task DownloadManifest(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: requesting audiobook manifest {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await contentDeliveryService.DownloadManifest(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Streams audiobook chapters directly from the source file for local audiobooks.
    /// The only supported format it MP3.
    /// The Kobo device has a ~60 second timeout on chapter downloads.
    /// </summary>
    [HttpGet("audiobook/{uuid:guid}/chapter/{partId:int}")]
    public async Task AudiobookChapter(string devicekey, Guid uuid, int partId)
    {
        logger.LogInformation("Kobo: requesting chapter {PartId} of {Uuid}", partId, uuid);

        var book = await contentDeliveryService.ResolveLocalAudiobook(uuid);
        if (book is null || partId < 0 || partId >= book.Chapters.Count)
        {
            HttpContext.Response.StatusCode = 404;
            return;
        }

        logger.LogInformation("AudiobookChapter: streaming chapter {PartId}/{TotalChapters} of '{Title}' ({Uuid}) as {Codec}", partId, book.Chapters.Count, book.Title, uuid, book.File.AudioCodec ?? "aac");
        HttpContext.Response.StatusCode = 200;
        HttpContext.Response.ContentType = "audio/mpeg";
        await contentDeliveryService.StreamAudiobookChapter(book, partId, HttpContext.Response.Body, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Serves cover images for local books. Unknown books are redirected (307) to Kobo's CDN.
    /// The incoming UUID is the cache-busting cover UUID v5 derived from BookUuid + ModifiedAt;
    /// legacy URLs still work because the service also falls back to BookUuid / EntitlementUuid.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{uuid:guid}/{width:int}/{height:int}/{greyscale}/image.jpg")]
    [HttpGet("{uuid:guid}/{width:int}/{height:int}/{quality:int}/{greyscale}/image.jpg")]
    public async Task CoverImage(string devicekey, Guid uuid, int width, int height, int? quality = null, string? greyscale = null)
    {
        logger.LogInformation("Kobo: requesting cover for {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await contentDeliveryService.CoverImage(request, uuid, width, height, quality?.ToString(), greyscale);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Handles reading state for local books. GET returns persisted progress, PUT stores it.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/library/{uuid}/state")]
    [HttpPut("v1/library/{uuid}/state")]
    public async Task ReadingState(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: {Method} reading state for {Uuid}", HttpContext.Request.Method, uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await readingStateService.ReadingState(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns metadata (title, authors, download URLs, series info) for local books.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/library/{uuid}/metadata")]
    public async Task Metadata(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: requesting metadata for {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await librarySyncService.Metadata(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Handles book deletion from the device. Local books are marked as archived (not physically deleted).
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpDelete("v1/library/{uuid}")]
    public async Task DeleteBook(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: deleting book {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await librarySyncService.DeleteBook(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns series info for local series. Store series are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/books/series/{uuid}")]
    public async Task Series(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: requesting series {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.Series(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns empty recommendations stub. No local equivalent to provide.
    /// </summary>
    [HttpGet("v1/user/recommendations")]
    public async Task Recommendations(string devicekey)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.Recommendations(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns next-read suggestions. Local books return an empty stub.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuid}/nextread")]
    public async Task NextRead(string devicekey, Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.NextRead(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns per-product recommendations. Local books return an empty stub.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuid}/recommendations")]
    public async Task ProductRecommendations(string devicekey, Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.ProductRecommendations(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns audiobook product details. Local audiobooks return metadata from Alexandria.
    /// Store-purchased audiobooks are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/audiobooks/{uuid}")]
    public async Task AudiobookProduct(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: requesting audiobook product {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.AudiobookProduct(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns product prices. Local books return price 0. Supports comma-separated UUIDs for batch lookups.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuids}/prices")]
    public async Task ProductPrices(string devicekey, string uuids)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.ProductPrices(request, uuids);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns ebook product details. Local ebooks return metadata from Alexandria.
    /// Store-purchased ebooks are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/books/{uuid:guid}")]
    public async Task EbookProduct(string devicekey, Guid uuid)
    {
        logger.LogInformation("Kobo: requesting ebook product {Uuid}", uuid);
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await productService.EbookProduct(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Intercepts analytics events to handle local book state changes (e.g. MarkAsFinished).
    /// Events for store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpPost("v1/analytics/event")]
    public async Task AnalyticsEvent(string devicekey)
    {
        logger.LogInformation("Kobo: processing analytics event");
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, devicekey);
        var response = await analyticsService.AnalyticsEvent(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}
