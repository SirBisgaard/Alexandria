namespace Alexandria.Api.Models.Events;

public record FileManagementFailureEvent
{
    public Guid BookUuid { get; init; }
    public string Error { get; init; } = string.Empty;
}
