namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Result of evaluating a filename template against the entire library. Surfaces the
/// intended rename operations, books that are already at the target name, and any
/// conflicts that must be resolved before a real run can proceed.
/// </summary>
public sealed record FileManagementPlan
{
    public string Template { get; init; } = string.Empty;
    public IReadOnlyList<FileManagementOperation> Operations { get; init; } = [];
    public IReadOnlyList<FileManagementSkipped> Skipped { get; init; } = [];
    public IReadOnlyList<FileManagementConflict> Conflicts { get; init; } = [];

    public bool HasConflicts => Conflicts.Count > 0;
}
