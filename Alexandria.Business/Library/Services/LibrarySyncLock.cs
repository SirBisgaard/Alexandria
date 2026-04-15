namespace Alexandria.Business.Library.Services;

/// <summary>
/// Singleton gate that ensures only one library sync runs at a time. The controller does a
/// non-blocking <see cref="TryAcquire"/> and returns 409 Conflict if already held, so repeated
/// clicks on the "Sync now" button don't stack work on top of an in-progress scan.
/// </summary>
public sealed class LibrarySyncLock
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public bool TryAcquire() => gate.Wait(0);

    public void Release() => gate.Release();
}
