using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class UserRepositoryTests : DatabaseTestBase
{
    private UserRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new UserRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task UserRepository_Add_RoundTripsThroughGetByUsername()
    {
        var uuid = Guid.NewGuid();
        await repository.Add(new User
        {
            UserUuid = uuid,
            Username = "Martin",
            Password = "fake:secret",
            DisplayName = "Martin Erik"
        });

        var fetched = await repository.GetByUsername("martin");

        Assert.IsNotNull(fetched);
        Assert.AreEqual(uuid, fetched.UserUuid);
        Assert.AreEqual("martin", fetched.Username);
        Assert.AreEqual("Martin Erik", fetched.DisplayName);
        Assert.AreEqual("fake:secret", fetched.Password);
    }

    [TestMethod]
    public async Task UserRepository_GetByUsername_IsCaseInsensitive()
    {
        await repository.Add(new User { UserUuid = Guid.NewGuid(), Username = "MixedCase", Password = "fake:x", DisplayName = "MC" });

        var lower = await repository.GetByUsername("mixedcase");
        var upper = await repository.GetByUsername("MIXEDCASE");

        Assert.IsNotNull(lower);
        Assert.IsNotNull(upper);
        Assert.AreEqual(lower.UserUuid, upper.UserUuid);
    }

    [TestMethod]
    public async Task UserRepository_GetByUuid_MissingUuid_ReturnsNull()
    {
        var fetched = await repository.GetByUuid(Guid.NewGuid());

        Assert.IsNull(fetched);
    }

    [TestMethod]
    public async Task UserRepository_GetByUsername_UnknownUser_ReturnsNull()
    {
        var fetched = await repository.GetByUsername("nobody");

        Assert.IsNull(fetched);
    }

    [TestMethod]
    public async Task UserRepository_GetAll_IncludesSeededAndAddedUsers()
    {
        await repository.Add(new User { UserUuid = Guid.NewGuid(), Username = "Second", Password = "fake:x", DisplayName = "Second User" });

        var all = await repository.GetAll();

        Assert.HasCount(2, all);
        Assert.Contains("alexander", all.Select(u => u.Username));
        Assert.Contains("second", all.Select(u => u.Username));
    }

    [TestMethod]
    public async Task UserRepository_Count_ReflectsInsertedRows()
    {
        Assert.AreEqual(1, await repository.Count());

        await repository.Add(new User { UserUuid = Guid.NewGuid(), Username = "Extra", Password = "fake:x", DisplayName = "Extra" });

        Assert.AreEqual(2, await repository.Count());
    }

    [TestMethod]
    public async Task UserRepository_Update_ModifiesDisplayNameAndPassword()
    {
        var uuid = Guid.NewGuid();
        await repository.Add(new User { UserUuid = uuid, Username = "Target", Password = "fake:old", DisplayName = "Old Name" });

        await repository.Update(new User { UserUuid = uuid, Username = "target", Password = "fake:new", DisplayName = "New Name" });

        var fetched = await repository.GetByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("fake:new", fetched.Password);
        Assert.AreEqual("New Name", fetched.DisplayName);
    }

    [TestMethod]
    public async Task UserRepository_Delete_RemovesTheUser()
    {
        var uuid = Guid.NewGuid();
        await repository.Add(new User { UserUuid = uuid, Username = "Doomed", Password = "fake:x", DisplayName = "Doomed" });

        await repository.Delete(uuid);

        Assert.IsNull(await repository.GetByUuid(uuid));
    }

    [TestMethod]
    public async Task UserRepository_UpdateLastLogin_BumpsLastLoginAt()
    {
        var uuid = Guid.NewGuid();
        await repository.Add(new User { UserUuid = uuid, Username = "Logger", Password = "fake:x", DisplayName = "L" });
        var before = (await repository.GetByUuid(uuid))!.LastLoginAt;

        await Task.Delay(10);
        await repository.UpdateLastLogin(uuid);

        var after = (await repository.GetByUuid(uuid))!.LastLoginAt;
        Assert.IsGreaterThan(before, after);
    }
}
