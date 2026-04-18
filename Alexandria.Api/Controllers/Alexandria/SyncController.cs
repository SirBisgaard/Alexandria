using Alexandria.Api.Models.Events;
using Alexandria.Api.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/sync")]
public class SyncController(IIndexingBroker broker, ILogger<SyncController> logger) : AlexandriaControllerBase
{
    /// <summary>
    /// Lightweight status check used by the sync popover on open. The frontend uses this to
    /// decide whether to show "Sync now" (idle) or "Syncing…" (running) and paints the current
    /// snapshot before subscribing to the SSE stream.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<SyncStatusResponse> GetStatus()
    {
        return new SyncStatusResponse(broker.IsRunning, broker.LastSnapshot);
    }

    /// <summary>
    /// Subscribes to the library-sync progress stream as SSE. If no run is in flight, enqueues
    /// one; if one is already running, attaches to it. Either way the client receives the same
    /// live event stream terminating in a <see cref="SyncCompleteEvent"/> or <see cref="SyncErrorEvent"/>.
    /// </summary>
    [HttpPost]
    public async Task Sync()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        using var subscription = broker.Subscribe();
        broker.TryEnqueueRun();

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
            logger.LogDebug("Sync SSE subscriber disconnected");
        }
    }
}
