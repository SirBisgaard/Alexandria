using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("/api/kobo")]
public class KoboDeviceController(IKoboProxyService koboProxyService) : ControllerBase
{
    // ── Initialization: merge our URLs into Kobo's response ──
    [HttpGet("v1/initialization")]
    public async Task Initialize()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.Initialization(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Sync: merge local books into Kobo's sync response ──
    [HttpGet("v1/library/sync")]
    public async Task LibrarySync()
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.LibrarySync(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Downloads: local book or proxy to Kobo ──
    [HttpGet("download/{uuid}/kepub")]
    public async Task Download(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.Download(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Covers: local book or proxy to Kobo CDN ──
    [HttpGet("{uuid}/{width}/{height}/{greyscale}/image.jpg")]
    [HttpGet("{uuid}/{width}/{height}/{quality}/{greyscale}/image.jpg")]
    public async Task CoverImage(Guid uuid, int width, int height, string? quality = null, string? greyscale = null)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.CoverImage(request, uuid, width, height, quality, greyscale);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Reading state: local book or proxy ──
    [HttpGet("v1/library/{uuid}/state")]
    [HttpPut("v1/library/{uuid}/state")]
    public async Task ReadingState(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.ReadingState(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Metadata: local or proxy ──
    [HttpGet("v1/library/{uuid}/metadata")]
    public async Task Metadata(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.Metadata(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }

    // ── Book deletion from device ──
    [HttpDelete("v1/library/{uuid}")]
    public async Task DeleteBook(Guid uuid)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.DeleteBook(request, uuid);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}