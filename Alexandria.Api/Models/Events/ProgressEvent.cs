namespace Alexandria.Api.Models.Events;

/// <summary>
/// Wire shape for a single progress tick. Shared by every SSE progress channel (library sync,
/// file management, etc.). <see cref="Title"/> / <see cref="Count"/> / <see cref="Total"/>
/// describe the outer activity; the <c>SubTask*</c> fields are populated when a nested
/// operation is active inside the tick.
/// </summary>
public record ProgressEvent
{
    public string Title { get; init; } = string.Empty;
    public int Count { get; init; }
    public int Total { get; init; }
    public string? SubTaskTitle { get; init; }
    public int? SubCount { get; init; }
    public int? SubTotal { get; init; }
}
