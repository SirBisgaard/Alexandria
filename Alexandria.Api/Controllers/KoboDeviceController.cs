using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Proxy;
using Alexandria.Business.Models.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

/// <summary>
/// Intercepts specific Kobo store API endpoints to inject local Alexandria library books.
/// Routes here take priority over <see cref="KoboController"/>'s catch-all.
/// Each action delegates to <see cref="IKoboProxyService"/> which decides whether to serve
/// local data or transparently proxy to storeapi.kobo.com.
/// </summary>
[ApiController]
[Route("/api/kobo")]
public class KoboDeviceController(IKoboProxyService koboProxyService, KoboProxySettings settings) : ControllerBase
{
    /// <summary>
    /// Intercepts /v1/initialization to rewrite Kobo's resource URLs so the device
    /// routes sync, image, and download requests back through Alexandria.
    /// </summary>
    [HttpGet("v1/initialization")]
    public async Task Initialize()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.Initialization(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Intercepts /v1/library/sync to merge local Alexandria books into Kobo's sync response.
    /// The device discovers new books to download through this endpoint.
    /// </summary>
    [HttpGet("v1/library/sync")]
    public async Task LibrarySync()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.LibrarySync(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Serves KEPUB file downloads for local books. Store-purchased book downloads are proxied to Kobo.
    /// </summary>
    [HttpGet("download/{uuid}/kepub")]
    public async Task Download(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.Download(request, uuid);
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
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.CoverImage(request, uuid, width, height, quality, greyscale);
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
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.ReadingState(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Returns metadata (title, authors, download URLs, series info) for local books.
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpGet("v1/library/{uuid}/metadata")]
    public async Task Metadata(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.Metadata(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    /// <summary>
    /// Handles book deletion from the device. Local books are marked as archived (not physically deleted).
    /// Store-purchased books are proxied to Kobo.
    /// </summary>
    [HttpDelete("v1/library/{uuid}")]
    public async Task DeleteBook(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request, settings.KoboRoutePrefix);
        var response = await koboProxyService.DeleteBook(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}
