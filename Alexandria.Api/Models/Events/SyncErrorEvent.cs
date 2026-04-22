namespace Alexandria.Api.Models.Events;

/// <summary>
/// Terminal SSE event emitted when library sync fails before producing a completion event.
/// The frontend treats <see cref="Done"/> + <see cref="Failed"/> as mutually exclusive with
/// <see cref="SyncCompleteEvent"/>'s success path and surfaces <see cref="Error"/> to the user.
/// </summary>
public record SyncErrorEvent
{
    public bool Done { get; init; }
    public bool Failed { get; init; }
    public string Error { get; init; } = string.Empty;
}
