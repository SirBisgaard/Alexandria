using Alexandria.Business.Integrations.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class AudioBookShelfSyncIntegrationRepositoryTests : DatabaseTestBase
{
    private AudioBookShelfSyncIntegrationRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new AudioBookShelfSyncIntegrationRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_GetByUserUuid_Missing_ReturnsNull()
    {
        Assert.IsNull(await repository.GetByUserUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_Upsert_Insert_RoundTripsAllFields()
    {
        var userUuid = await SeededUserUuid();
        var integration = new AudioBookShelfIntegration
        {
            UserUuid = userUuid,
            Enabled = true,
            ServerUrl = "https://abs.example.com",
            ApiToken = "secret-token"
        };

        await repository.Upsert(integration);

        var fetched = await repository.GetByUserUuid(userUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(userUuid, fetched.UserUuid);
        Assert.IsTrue(fetched.Enabled);
        Assert.AreEqual("https://abs.example.com", fetched.ServerUrl);
        Assert.AreEqual("secret-token", fetched.ApiToken);
        Assert.IsNull(fetched.LastSyncedAt);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_Upsert_Existing_UpdatesEnabledAndCredentials()
    {
        var userUuid = await SeededUserUuid();
        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = userUuid, Enabled = true, ServerUrl = "https://old", ApiToken = "old" });

        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = userUuid, Enabled = false, ServerUrl = "https://new", ApiToken = "new" });

        var fetched = await repository.GetByUserUuid(userUuid);
        Assert.IsNotNull(fetched);
        Assert.IsFalse(fetched.Enabled);
        Assert.AreEqual("https://new", fetched.ServerUrl);
        Assert.AreEqual("new", fetched.ApiToken);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_Upsert_PreservesLastSyncedAtAcrossConfigEdits()
    {
        var userUuid = await SeededUserUuid();
        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = userUuid, Enabled = true, ServerUrl = "https://x", ApiToken = "t" });
        var stamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        await repository.UpdateLastSynced(userUuid, stamp);

        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = userUuid, Enabled = false, ServerUrl = "https://x", ApiToken = "t" });

        var fetched = await repository.GetByUserUuid(userUuid);
        Assert.IsNotNull(fetched);
        Assert.IsNotNull(fetched.LastSyncedAt);
        Assert.AreEqual(stamp.ToUnixTimeSeconds(), fetched.LastSyncedAt.Value.ToUnixTimeSeconds());
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_GetAllEnabled_ExcludesDisabledRows()
    {
        var enabledUuid = await SeededUserUuid();
        var disabledUuid = await CreateAdditionalUser("disabled-user");

        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = enabledUuid, Enabled = true, ServerUrl = "https://e", ApiToken = "e" });
        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = disabledUuid, Enabled = false, ServerUrl = "https://d", ApiToken = "d" });

        var enabled = await repository.GetAllEnabled();

        Assert.HasCount(1, enabled);
        Assert.AreEqual(enabledUuid, enabled[0].UserUuid);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_UpdateLastSynced_StampsTheRow()
    {
        var userUuid = await SeededUserUuid();
        await repository.Upsert(new AudioBookShelfIntegration { UserUuid = userUuid, Enabled = true, ServerUrl = "https://x", ApiToken = "t" });
        Assert.IsNull((await repository.GetByUserUuid(userUuid))!.LastSyncedAt);

        var stamp = DateTimeOffset.UtcNow;
        await repository.UpdateLastSynced(userUuid, stamp);

        var fetched = await repository.GetByUserUuid(userUuid);
        Assert.IsNotNull(fetched);
        Assert.IsNotNull(fetched.LastSyncedAt);
        Assert.AreEqual(stamp.ToUnixTimeSeconds(), fetched.LastSyncedAt.Value.ToUnixTimeSeconds());
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_GetBookSyncedStates_EmptyForNewUser_ReturnsEmptyDictionary()
    {
        var userUuid = await SeededUserUuid();

        var states = await repository.GetBookSyncedStates(userUuid);

        Assert.IsEmpty(states);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_UpsertBookSyncedState_RoundTripsTimestamp()
    {
        var userUuid = await SeededUserUuid();
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        var ts = DateTimeOffset.UtcNow.AddMinutes(-3);

        await repository.UpsertBookSyncedState(userUuid, bookUuid, ts);

        var states = await repository.GetBookSyncedStates(userUuid);
        Assert.HasCount(1, states);
        Assert.IsTrue(states.ContainsKey(bookUuid));
        Assert.AreEqual(ts.ToUnixTimeSeconds(), states[bookUuid].ToUnixTimeSeconds());
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_UpsertBookSyncedState_Existing_UpdatesTimestamp()
    {
        var userUuid = await SeededUserUuid();
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.UpsertBookSyncedState(userUuid, bookUuid, DateTimeOffset.UtcNow.AddHours(-1));

        var newer = DateTimeOffset.UtcNow;
        await repository.UpsertBookSyncedState(userUuid, bookUuid, newer);

        var states = await repository.GetBookSyncedStates(userUuid);
        Assert.HasCount(1, states);
        Assert.AreEqual(newer.ToUnixTimeSeconds(), states[bookUuid].ToUnixTimeSeconds());
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_DeleteBookSyncedState_RemovesTheRow()
    {
        var userUuid = await SeededUserUuid();
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.UpsertBookSyncedState(userUuid, bookUuid, DateTimeOffset.UtcNow);

        await repository.DeleteBookSyncedState(userUuid, bookUuid);

        var states = await repository.GetBookSyncedStates(userUuid);
        Assert.IsEmpty(states);
    }

    [TestMethod]
    public async Task AudioBookShelfSyncIntegrationRepository_GetBookSyncedStates_ScopedToUser()
    {
        var userA = await SeededUserUuid();
        var userB = await CreateAdditionalUser("second-user");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);

        await repository.UpsertBookSyncedState(userA, bookUuid, DateTimeOffset.UtcNow);

        var statesA = await repository.GetBookSyncedStates(userA);
        var statesB = await repository.GetBookSyncedStates(userB);

        Assert.HasCount(1, statesA);
        Assert.IsEmpty(statesB);
    }

    /// <summary>Reads the seeded "alexander" user's UUID — V1 migration plants exactly one Users row.</summary>
    private async Task<Guid> SeededUserUuid()
    {
        return await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UserUuid FROM Users LIMIT 1";
            return Guid.Parse((string)cmd.ExecuteScalar()!);
        });
    }

    /// <summary>Inserts a second Users row so per-user scope tests have someone to compare against.</summary>
    private async Task<Guid> CreateAdditionalUser(string username)
    {
        var uuid = Guid.NewGuid();
        await ConnectionProvider.Write(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt) VALUES (@u, @n, 'fake:x', 'x', @t, @t, @t)";
            cmd.Parameters.AddWithValue("@u", uuid.ToString());
            cmd.Parameters.AddWithValue("@n", username);
            cmd.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });
        return uuid;
    }
}
