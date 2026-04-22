namespace Alexandria.Api.Models.Events;

public record FileManagementProgressEvent
{
    public string Title { get; init; } = string.Empty;
    public int Progress { get; init; }
    public int Total { get; init; }
}
