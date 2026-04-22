namespace Alexandria.Api.Models.Events;

public record FileManagementCompleteEvent
{
    public bool Done { get; init; }
    public int Renamed { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<FileManagementFailureEvent> Failed { get; init; } = [];
}
