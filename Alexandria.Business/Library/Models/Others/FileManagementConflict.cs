namespace Alexandria.Business.Library.Models.Others;

public sealed record FileManagementConflict
{
    public FileManagementConflictKind Kind { get; init; }
    public string Path { get; init; } = string.Empty;
    public IReadOnlyList<Guid> BookUuids { get; init; } = [];
}
