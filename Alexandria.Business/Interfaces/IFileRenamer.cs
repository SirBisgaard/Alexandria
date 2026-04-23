using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Interfaces;

/// <summary>
/// Per-format file-move primitive. One implementation per source layout, dispatched via
/// <see cref="CanHandle"/>. The two-phase <see cref="PlanRename"/> / <see cref="Execute"/>
/// split lets <c>FileManagementService</c> compute all operations during dry-run (no I/O)
/// and then commit them one at a time.
/// </summary>
public interface IFileRenamer
{
    /// <summary>
    /// Returns true if this renamer handles the given source path. File-based renamers check
    /// the extension; folder-based renamers inspect directory layout.
    /// </summary>
    bool CanHandle(string path);

    /// <summary>
    /// Pure computation — returns the exact filesystem moves that would happen for
    /// <paramref name="newBaseName"/>. No I/O.
    /// </summary>
    FileRenameStep PlanRename(Book book, string newBaseName);

    /// <summary>
    /// Executes <paramref name="step"/> on disk. Transactional: on any failure all completed
    /// moves within this step are rolled back so the filesystem is left at its prior state.
    /// Rollback of a successful Execute (e.g. because the subsequent DB write failed) is done
    /// by the caller calling <c>Execute(step.Invert())</c>. <paramref name="subProgress"/>, when
    /// provided, receives per-chapter-file progress ticks (only meaningful for folder-based
    /// audiobook renamers).
    /// </summary>
    void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null);
}
