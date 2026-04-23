using Alexandria.Api.Models.Events;
using Alexandria.Api.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/sync")]
public class SyncController(ILibraryTaskBroker broker, ILogger<SyncController> logger) : AlexandriaControllerBase
{
    /// <summary>
    /// Subscribes to the library-sync progress stream as SSE. If no run is in flight, enqueues
    /// a <see cref="SyncTask"/>; if one is already running, attaches to it. Either way the client
    /// receives the same live event stream terminating in a <see cref="SyncCompleteEvent"/> or
    /// <see cref="SyncErrorEvent"/>.
    /// </summary>
    [HttpPost]
    public async Task Sync()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        using var subscription = broker.Subscribe();
        broker.TryEnqueue(typeof(SyncTask));

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
