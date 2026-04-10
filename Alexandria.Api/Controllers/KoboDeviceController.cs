using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

/// <summary>
/// Intercepts specific Kobo store API endpoints to inject local Alexandria library books.
/// Routes here take priority over <see cref="KoboController"/>'s catch-all.
/// </summary>
[ApiController]
[Route("/api/kobo")]
public class KoboDeviceController(IKoboLibrarySyncService librarySyncService, IKoboContentDeliveryService contentDeliveryService, IKoboReadingStateService readingStateService, IKoboProductService productService) : ControllerBase
{
    /// <summary>
    /// Intercepts /v1/initialization to rewrite Kobo's resource URLs so the device
    /// routes sync, image, and download requests back through Alexandria.
    /// </summary>
    [HttpGet("v1/initialization")]
    public async Task Initialize()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await librarySyncService.Initialization(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Intercepts /v1/library/sync to merge local Alexandria books into Kobo's sync response.
    /// The device discovers new books to download through this endpoint.
    /// </summary>
    [HttpGet("v1/library/sync")]
    public async Task LibrarySync()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await librarySyncService.LibrarySync(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves KEPUB file downloads for local books. Store-purchased book downloads are proxied to Kobo.
    /// </summary>
    [HttpGet("download/{uuid}/kepub")]
    public async Task Download(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await contentDeliveryService.Download(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves the audiobook manifest JSON for local audiobooks. Contains chapter list with download URLs.
    /// Store-purchased audiobook manifests are proxied to Kobo.
    /// </summary>
    [HttpGet("download/{uuid}/manifest")]
    public async Task DownloadManifest(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await contentDeliveryService.DownloadManifest(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves individual MP3 chapter files for local audiobooks.
    /// Store-purchased audiobook chapters are proxied to Kobo.
    /// </summary>
    [HttpGet("audiobook/{uuid}/chapter/{partId}")]
    public async Task AudiobookChapter(Guid uuid, int partId)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await contentDeliveryService.AudiobookChapter(request, uuid, partId);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves cover images for local books. Unknown books are redirected (307) to Kobo's CDN.
    /// Matches the image_url_template rewritten during initialization.
    /// </summary>
    [HttpGet("{uuid}/{width}/{height}/{greyscale}/image.jpg")]
    [HttpGet("{uuid}/{width}/{height}/{quality}/{greyscale}/image.jpg")]
    public async Task CoverImage(Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await contentDeliveryService.CoverImage(request, uuid, width, height, quality, greyscale);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Handles reading state for local books. GET returns persisted progress, PUT stores it.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/library/{uuid}/state")]
    [HttpPut("v1/library/{uuid}/state")]
    public async Task ReadingState(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await readingStateService.ReadingState(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns metadata (title, authors, download URLs, series info) for local books.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/library/{uuid}/metadata")]
    public async Task Metadata(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await librarySyncService.Metadata(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Handles book deletion from the device. Local books are marked as archived (not physically deleted).
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpDelete("v1/library/{uuid}")]
    public async Task DeleteBook(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await librarySyncService.DeleteBook(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns series info for local series. Store series are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/books/series/{uuid}")]
    public async Task Series(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.Series(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns empty recommendations stub. No local equivalent to provide.
    /// </summary>
    [HttpGet("v1/user/recommendations")]
    public async Task Recommendations()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.Recommendations(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns next-read suggestions. Local books return an empty stub.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuid}/nextread")]
    public async Task NextRead(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.NextRead(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns per-product recommendations. Local books return an empty stub.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuid}/recommendations")]
    public async Task ProductRecommendations(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.ProductRecommendations(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns audiobook product details. Local audiobooks return metadata from Alexandria.
    /// Store-purchased audiobooks are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/audiobooks/{uuid}")]
    public async Task AudiobookProduct(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.AudiobookProduct(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns product prices. Local books return price 0. Supports comma-separated UUIDs for batch lookups.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/{uuids}/prices")]
    public async Task ProductPrices(string uuids)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.ProductPrices(request, uuids);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns ebook product details. Local ebooks return metadata from Alexandria.
    /// Store-purchased ebooks are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/products/books/{uuid}")]
    public async Task EbookProduct(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await productService.EbookProduct(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}
