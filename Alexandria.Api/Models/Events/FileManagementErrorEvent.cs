using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Models.Events;

public record FileManagementErrorEvent
{
    public bool Done { get; init; }
    public bool Failed { get; init; }
    public string Error { get; init; } = string.Empty;
    public IReadOnlyList<FileManagementConflict>? Conflicts { get; init; }
}
