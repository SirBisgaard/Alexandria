namespace Alexandria.Api.Models.Events;

/// <summary>
/// Response shape for <c>GET /api/alexandria/sync/status</c>. The sync popover calls this on open
/// to decide the button label ("Sync now" vs "Syncing…") and paint the current progress frame
/// before opening an SSE stream to receive live updates.
/// </summary>
public record SyncStatusResponse(bool Running, SyncProgressEvent? Snapshot);
