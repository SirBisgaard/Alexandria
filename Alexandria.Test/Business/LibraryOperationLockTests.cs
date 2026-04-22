using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class LibraryOperationLockTests
{
    [TestMethod]
    public void TryBeginExclusive_WhenIdle_AcquiresAndMarksBusy()
    {
        var lockService = new LibraryOperationLock();

        var acquired = lockService.TryBeginExclusive(out var lease);

        Assert.IsTrue(acquired);
        Assert.IsTrue(lockService.IsBusy);
        lease.Dispose();
        Assert.IsFalse(lockService.IsBusy);
    }

    [TestMethod]
    public void TryBeginExclusive_WhenBusy_Fails()
    {
        var lockService = new LibraryOperationLock();
        Assert.IsTrue(lockService.TryBeginExclusive(out var first));

        var acquired = lockService.TryBeginExclusive(out _);

        Assert.IsFalse(acquired);
        first.Dispose();
    }

    [TestMethod]
    public void TryBeginExclusive_AfterDispose_CanBeReacquired()
    {
        var lockService = new LibraryOperationLock();

        Assert.IsTrue(lockService.TryBeginExclusive(out var lease1));
        lease1.Dispose();

        Assert.IsTrue(lockService.TryBeginExclusive(out var lease2));
        lease2.Dispose();
    }

    [TestMethod]
    public void LeaseDispose_IsIdempotent()
    {
        var lockService = new LibraryOperationLock();
        Assert.IsTrue(lockService.TryBeginExclusive(out var lease));

        lease.Dispose();
        lease.Dispose();

        Assert.IsFalse(lockService.IsBusy);
    }

    [TestMethod]
    public void IsBusy_ReflectsCurrentLeaseState()
    {
        var lockService = new LibraryOperationLock();
        Assert.IsFalse(lockService.IsBusy);

        lockService.TryBeginExclusive(out var lease);
        Assert.IsTrue(lockService.IsBusy);

        lease.Dispose();
        Assert.IsFalse(lockService.IsBusy);
    }
}
