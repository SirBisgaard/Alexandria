namespace Alexandria.Api.Models.Responses;

public record FileManagementOperationResponse
{
    public Guid BookUuid { get; init; }
    public string Title { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public string CurrentPath { get; init; } = string.Empty;
    public string NewPath { get; init; } = string.Empty;
    public int ChapterRenameCount { get; init; }
}
