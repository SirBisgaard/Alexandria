namespace Alexandria.Api.Models.Events;

/// <summary>
/// Wire shape for a single indexing progress tick. <see cref="Title"/> / <see cref="Subtitle"/>
/// / <see cref="Progress"/> / <see cref="Total"/> mirror the outer <c>IndexingReport</c>;
/// <see cref="SubTaskTitle"/> / <see cref="SubProgress"/> / <see cref="SubTotal"/> are
/// populated when a nested long-running operation (e.g. chapter transcoding) is active.
/// </summary>
public record SyncProgressEvent(
    string Title,
    string? Subtitle,
    int Progress,
    int Total,
    string? SubTaskTitle,
    int? SubProgress,
    int? SubTotal);
