using Alexandria.DataAccess.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class DatabaseInitializerTests : DatabaseTestBase
{
    private static readonly string[] ExpectedTables =
    [
        "Series",
        "Authors",
        "Books",
        "BookFiles",
        "BookIdentifiers",
        "BookSubjects",
        "BookContributors",
        "AudiobookChapters",
        "BookSeries",
        "BookAuthors",
        "KoboReadingStates",
        "Collections",
        "CollectionBooks",
        "KoboDeviceSyncedFiles",
        "KoboDevices",
        "KoboTrafficLogs",
        "Settings",
        "Users",
        "SchemaVersion"
    ];

    [TestMethod]
    public async Task DatabaseInitializer_FreshDb_CreatesAllExpectedTables()
    {
        var actualTables = await ReadTableNames();

        foreach (var expected in ExpectedTables)
        {
            Assert.IsTrue(actualTables.Contains(expected), $"Expected table '{expected}' to exist after initialization.");
        }
    }

    [TestMethod]
    public async Task DatabaseInitializer_FreshDb_SchemaVersionIs1()
    {
        var version = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(Version) FROM SchemaVersion";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        Assert.AreEqual(1, version);
    }

    [TestMethod]
    public async Task DatabaseInitializer_FreshDb_SeedsSettingsRowWithUuidSeed()
    {
        var (count, uuidSeed) = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), UuidSeed FROM Settings";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return (reader.GetInt32(0), reader.GetString(1));
        });

        Assert.AreEqual(1, count);
        Assert.IsTrue(Guid.TryParse(uuidSeed, out var parsed) && parsed != Guid.Empty, "UuidSeed must be a non-empty GUID.");
    }

    [TestMethod]
    public async Task DatabaseInitializer_FreshDb_SeedsDefaultUserAlexander()
    {
        var (username, displayName, passwordHash) = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Username, DisplayName, Password FROM Users";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        });

        Assert.AreEqual("alexander", username);
        Assert.AreEqual("Alexander The Great", displayName);
        Assert.AreEqual("fake:TheGreat", passwordHash);
    }

    [TestMethod]
    public async Task DatabaseInitializer_RunTwice_IsIdempotent()
    {
        var secondInitializer = new DatabaseInitializer(ConnectionProvider, new FakePasswordHasher(), Settings, NullLogger<DatabaseInitializer>.Instance);
        await secondInitializer.Initialize();

        var versionRowCount = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersion";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
        var userCount = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
        var settingsCount = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Settings";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        Assert.AreEqual(1, versionRowCount, "Re-running Initialize should not append a second schema-version row.");
        Assert.AreEqual(1, userCount, "Re-running Initialize should not insert a duplicate default user.");
        Assert.AreEqual(1, settingsCount, "Re-running Initialize should not insert a duplicate Settings row.");
    }

    private Task<HashSet<string>> ReadTableNames() =>
        ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = cmd.ExecuteReader();
            var names = new HashSet<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));
            return names;
        });
}
