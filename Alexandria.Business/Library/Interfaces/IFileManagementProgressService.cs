using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

/// <summary>
/// Scoped progress broker for a file-management run. Parallels <see cref="IIndexingProgressService"/>
/// but has its own frame and completion shape so the frontend can branch on a dedicated SSE
/// channel without union-typing indexing frames.
/// </summary>
public interface IFileManagementProgressService
{
    void Subscribe(IProgress<FileManagementReport> onProgress, IProgress<FileManagementCompletion> onCompletion);

    /// <summary>Emits a progress tick for a per-book rename.</summary>
    void ReportRenaming(string bookTitle, int ordinal, int total);

    /// <summary>Emits the terminal completion frame.</summary>
    void ReportDone(int renamed, int skipped, IReadOnlyList<FileManagementFailure> failed);
}
