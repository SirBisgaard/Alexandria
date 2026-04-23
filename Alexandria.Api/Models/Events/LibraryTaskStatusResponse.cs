namespace Alexandria.Api.Models.Events;

/// <summary>
/// Response shape for <c>GET /api/alexandria/library-task/status</c>. Consumers (sync popover,
/// manage-files modal) call this on open to decide: show "busy" if another flow is running,
/// attach to the live stream if their own flow is running, or enable the trigger button if idle.
/// </summary>
public record LibraryTaskStatusResponse
{
    public bool Running { get; init; }
    public string? Kind { get; init; }
    public ProgressEvent? Snapshot { get; init; }
}
