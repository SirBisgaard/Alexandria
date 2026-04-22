namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Progress frame emitted during a file-management run. <see cref="Title"/> is the book currently
/// being renamed. Ordinal/Total count books moved so far against the total planned.
/// </summary>
public sealed record FileManagementReport
{
    public string Title { get; init; } = string.Empty;
    public int Ordinal { get; init; }
    public int Total { get; init; }
}
