using Alexandria.Api.Models.Events;
using Alexandria.Api.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/library-task")]
public class LibraryTaskController(ILibraryTaskBroker broker) : AlexandriaControllerBase
{
    /// <summary>
    /// Lightweight status check used by every library-task consumer (sync popover,
    /// manage-files modal) on open. The frontend uses <c>kind</c> to decide whether to
    /// attach to the live stream (own flow), show "busy" (other flow), or enable its
    /// trigger button (idle).
    /// </summary>
    [HttpGet("status")]
    public ActionResult<LibraryTaskStatusResponse> GetStatus()
    {
        return new LibraryTaskStatusResponse
        {
            Running = broker.IsRunning,
            Kind = broker.RunningKind,
            Snapshot = broker.LastSnapshot
        };
    }
}
