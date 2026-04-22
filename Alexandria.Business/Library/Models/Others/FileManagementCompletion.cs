namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Terminal event for a completed file-management run.
/// </summary>
public sealed record FileManagementCompletion
{
    public int Renamed { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<FileManagementFailure> Failed { get; init; } = [];
}
