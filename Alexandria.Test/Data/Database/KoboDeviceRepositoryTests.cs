using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class KoboDeviceRepositoryTests : DatabaseTestBase
{
    private KoboDeviceRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new KoboDeviceRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_Create_RoundTripsNicknameAndUuid()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Living Room Kobo" });

        var fetched = await repository.GetByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(uuid, fetched.DeviceUuid);
        Assert.AreEqual("Living Room Kobo", fetched.Nickname);
        Assert.IsFalse(fetched.IsLocked);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_GetByUuid_Missing_ReturnsNull()
    {
        Assert.IsNull(await repository.GetByUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task KoboDeviceRepository_GetAll_ReturnsAllCreatedDevices()
    {
        await repository.Create(new KoboDevice { DeviceUuid = Guid.NewGuid(), Nickname = "A" });
        await repository.Create(new KoboDevice { DeviceUuid = Guid.NewGuid(), Nickname = "B" });

        var all = await repository.GetAll();

        Assert.HasCount(2, all);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_Update_ModifiesNickname()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Old" });

        await repository.Update(new KoboDevice { DeviceUuid = uuid, Nickname = "New" });

        var fetched = await repository.GetByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("New", fetched.Nickname);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_Delete_RemovesDevice()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Doomed" });

        await repository.Delete(uuid);

        Assert.IsNull(await repository.GetByUuid(uuid));
    }

    [TestMethod]
    public async Task KoboDeviceRepository_UpdateLastSynced_BumpsTimestamp()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Tracked" });
        var before = (await repository.GetByUuid(uuid))!.LastSyncedAt;

        await Task.Delay(10);
        await repository.UpdateLastSynced(uuid);

        var after = (await repository.GetByUuid(uuid))!.LastSyncedAt;
        Assert.IsGreaterThan(before, after);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_SetLocked_TogglesIsLockedFlag()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Toggle Me" });

        await repository.SetLocked(uuid, true);
        Assert.IsTrue((await repository.GetByUuid(uuid))!.IsLocked);

        await repository.SetLocked(uuid, false);
        Assert.IsFalse((await repository.GetByUuid(uuid))!.IsLocked);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_BindDevice_PersistsSerialAndHardwareMetadata()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Unbound" });

        await repository.BindDevice(uuid, "SN123456", "Kobo Libra Colour", "0387", "4.39.21908");

        var fetched = await repository.GetByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("SN123456", fetched.SerialNumber);
        Assert.AreEqual("Kobo Libra Colour", fetched.ModelName);
        Assert.AreEqual("0387", fetched.ModelCode);
        Assert.AreEqual("4.39.21908", fetched.Firmware);
    }

    [TestMethod]
    public async Task KoboDeviceRepository_BindDevice_NullFields_PreserveExistingValues()
    {
        var uuid = Guid.NewGuid();
        await repository.Create(new KoboDevice { DeviceUuid = uuid, Nickname = "Bound" });
        await repository.BindDevice(uuid, "SN1", "Model A", "0387", "fw1");

        await repository.BindDevice(uuid, "SN2", modelName: null, modelCode: null, firmware: null);

        var fetched = await repository.GetByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("SN2", fetched.SerialNumber);
        Assert.AreEqual("Model A", fetched.ModelName);
        Assert.AreEqual("0387", fetched.ModelCode);
        Assert.AreEqual("fw1", fetched.Firmware);
    }
}
