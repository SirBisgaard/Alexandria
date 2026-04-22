using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Models.Responses;

public record FileManagementPlanResponse
{
    public string Template { get; init; } = string.Empty;
    public IReadOnlyList<FileManagementOperationResponse> Operations { get; init; } = [];
    public IReadOnlyList<FileManagementSkippedResponse> Skipped { get; init; } = [];
    public IReadOnlyList<FileManagementConflict> Conflicts { get; init; } = [];
}
