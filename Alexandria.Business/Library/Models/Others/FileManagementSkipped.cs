namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A book whose current filename already matches the template's output for its metadata —
/// no rename needed.
/// </summary>
public sealed record FileManagementSkipped
{
    public Guid BookUuid { get; init; }
    public string Title { get; init; } = string.Empty;
    public string CurrentPath { get; init; } = string.Empty;
}
