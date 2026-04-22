namespace Alexandria.Business.Interfaces;

/// <summary>
/// The single at-most-one-in-flight gate for heavy library operations (indexing + file management).
/// Both consumers call <see cref="TryBeginExclusive"/> to reserve the slot; the returned lease is
/// released on <c>Dispose</c>. <see cref="IsBusy"/> is the source of truth behind the UI's
/// "running" signal.
/// </summary>
public interface ILibraryOperationLock
{
    /// <summary>True while a lease is held by any caller.</summary>
    bool IsBusy { get; }

    /// <summary>Reserves the slot. Returns false (and leaves <paramref name="lease"/> undefined) if another operation is in flight.</summary>
    bool TryBeginExclusive(out IDisposable lease);
}
