using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface IFileManagementService
{
    /// <summary>
    /// Reads the persisted template from Settings, walks the library, renders each book's target
    /// filename, and detects collisions. No filesystem or database writes.
    /// </summary>
    Task<FileManagementPlan> ComputePlan();

    /// <summary>
    /// Executes the current plan. Recomputes the plan against live DB + FS state and aborts with
    /// <see cref="FileManagementConflictException"/> if any conflict is present. Yields progress
    /// through the scoped <see cref="IFileManagementProgressService"/>.
    /// </summary>
    Task Run(CancellationToken cancellationToken);
}

public sealed class FileManagementConflictException(IReadOnlyList<FileManagementConflict> conflicts) : Exception("Conflicts detected.")
{
    public IReadOnlyList<FileManagementConflict> Conflicts { get; } = conflicts;
}
