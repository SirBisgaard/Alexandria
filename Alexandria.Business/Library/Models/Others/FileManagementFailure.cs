namespace Alexandria.Business.Library.Models.Others;

public sealed record FileManagementFailure
{
    public Guid BookUuid { get; init; }
    public string Error { get; init; } = string.Empty;
}
