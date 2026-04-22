namespace Alexandria.Api.Models.Events;

/// <summary>
/// Wire shape for a single indexing progress tick. <see cref="Title"/> / <see cref="Subtitle"/>
/// / <see cref="Progress"/> / <see cref="Total"/> mirror the outer <c>IndexingReport</c>;
/// <see cref="SubTaskTitle"/> / <see cref="SubProgress"/> / <see cref="SubTotal"/> are
/// populated when a nested long-running operation (e.g. chapter transcoding) is active.
/// </summary>
public record SyncProgressEvent
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public int Progress { get; init; }
    public int Total { get; init; }
    public string? SubTaskTitle { get; init; }
    public int? SubProgress { get; init; }
    public int? SubTotal { get; init; }
}
