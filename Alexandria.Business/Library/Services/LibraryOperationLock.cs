using Alexandria.Business.Interfaces;

namespace Alexandria.Business.Library.Services;

/// <summary>
/// Singleton gate for heavy library operations. Indexing runs and file-management runs
/// both call <see cref="TryBeginExclusive"/> and share the same slot — so the UI's
/// "running" signal stays a single boolean no matter which op is in flight.
/// </summary>
public sealed class LibraryOperationLock : ILibraryOperationLock
{
    private readonly Lock gate = new();
    private bool busy;

    public bool IsBusy
    {
        get
        {
            lock (gate)
                return busy;
        }
    }

    public bool TryBeginExclusive(out IDisposable lease)
    {
        lock (gate)
        {
            if (busy)
            {
                lease = null!;
                return false;
            }
            busy = true;
        }
        lease = new Lease(this);
        return true;
    }

    private sealed class Lease(LibraryOperationLock owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            lock (owner.gate)
                owner.busy = false;
        }
    }
}
