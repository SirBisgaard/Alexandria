using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class KoboDeviceSyncRepositoryTests : DatabaseTestBase
{
    private KoboDeviceSyncRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new KoboDeviceSyncRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task KoboDeviceSyncRepository_UpsertSyncedFile_Insert_PersistsRow()
    {
        var deviceUuid = Guid.NewGuid().ToString();
        var bookUuid = Guid.NewGuid();
        var modifiedAt = new DateTimeOffset(2026, 4, 17, 10, 0, 0, TimeSpan.Zero);

        await repository.UpsertSyncedFile(deviceUuid, bookUuid, modifiedAt);

        var synced = await repository.GetSyncedModifiedDates(deviceUuid);
        Assert.HasCount(1, synced);
        Assert.AreEqual(modifiedAt, synced[bookUuid]);
    }

    [TestMethod]
    public async Task KoboDeviceSyncRepository_UpsertSyncedFile_SameKey_UpdatesBookModifiedAt()
    {
        var deviceUuid = Guid.NewGuid().ToString();
        var bookUuid = Guid.NewGuid();
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.UpsertSyncedFile(deviceUuid, bookUuid, first);

        await repository.UpsertSyncedFile(deviceUuid, bookUuid, second);

        var synced = await repository.GetSyncedModifiedDates(deviceUuid);
        Assert.HasCount(1, synced);
        Assert.AreEqual(second, synced[bookUuid]);
    }

    [TestMethod]
    public async Task KoboDeviceSyncRepository_GetSyncedModifiedDates_FiltersByDevice()
    {
        var deviceA = Guid.NewGuid().ToString();
        var deviceB = Guid.NewGuid().ToString();
        var modifiedAt = DateTimeOffset.UtcNow;

        await repository.UpsertSyncedFile(deviceA, Guid.NewGuid(), modifiedAt);
        await repository.UpsertSyncedFile(deviceA, Guid.NewGuid(), modifiedAt);
        await repository.UpsertSyncedFile(deviceB, Guid.NewGuid(), modifiedAt);

        Assert.HasCount(2, await repository.GetSyncedModifiedDates(deviceA));
        Assert.HasCount(1, await repository.GetSyncedModifiedDates(deviceB));
    }

    [TestMethod]
    public async Task KoboDeviceSyncRepository_GetSyncedModifiedDates_UnknownDevice_ReturnsEmpty()
    {
        var synced = await repository.GetSyncedModifiedDates(Guid.NewGuid().ToString());

        Assert.IsEmpty(synced);
    }

    [TestMethod]
    public async Task KoboDeviceSyncRepository_RemoveSyncedFile_RemovesOnlyTargetedPair()
    {
        var deviceUuid = Guid.NewGuid().ToString();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await repository.UpsertSyncedFile(deviceUuid, keep, DateTimeOffset.UtcNow);
        await repository.UpsertSyncedFile(deviceUuid, drop, DateTimeOffset.UtcNow);

        await repository.RemoveSyncedFile(deviceUuid, drop);

        var synced = await repository.GetSyncedModifiedDates(deviceUuid);
        Assert.HasCount(1, synced);
        Assert.IsTrue(synced.ContainsKey(keep));
    }
}
