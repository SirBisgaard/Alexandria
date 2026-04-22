namespace Alexandria.Api.Models.Responses;

public record FileManagementSkippedResponse
{
    public Guid BookUuid { get; init; }
    public string Title { get; init; } = string.Empty;
    public string CurrentPath { get; init; } = string.Empty;
}
