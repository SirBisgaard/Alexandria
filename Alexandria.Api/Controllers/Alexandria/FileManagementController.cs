using Alexandria.Api.Models;
using Alexandria.Api.Sync;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/file-management")]
public class FileManagementController(IFileManagementService renameService, ILibraryTaskBroker broker, ILogger<FileManagementController> logger) : AlexandriaControllerBase
{
    [HttpPost("dry-run")]
    public async Task<IActionResult> DryRun()
    {
        try
        {
            var plan = await renameService.ComputePlan();
            return Ok(ApiMapper.ToFileManagementPlanResponse(plan));
        }
        catch (TemplateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Subscribes to the file-management progress stream as SSE. If no run is in flight, enqueues
    /// a <see cref="FileManagementTask"/>; if one is already running, attaches to it. The actual
    /// work runs on the shared background service, so the rename survives client disconnect.
    /// </summary>
    [HttpPost]
    public async Task Run()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        using var subscription = broker.Subscribe();
        broker.TryEnqueue(typeof(FileManagementTask));

        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(HttpContext.RequestAborted))
            {
                await Response.WriteAsync(message);
                await Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("File-management SSE subscriber disconnected");
        }
    }

}
