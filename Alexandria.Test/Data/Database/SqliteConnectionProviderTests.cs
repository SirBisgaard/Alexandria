using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database;
using Microsoft.Data.Sqlite;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class SqliteConnectionProviderTests : DatabaseTestBase
{
    [TestMethod]
    public async Task ConnectionProvider_OpensDatabaseInWalMode()
    {
        var journalMode = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode";
            return (string)cmd.ExecuteScalar()!;
        });

        Assert.AreEqual("wal", journalMode);
    }

    [TestMethod]
    public async Task Write_IsImmediatelyVisibleToASecondConnection()
    {
        var probeUsername = $"probe-{Guid.NewGuid():N}";
        await ConnectionProvider.Write(conn =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt) VALUES (@u, @n, 'fake:x', 'Probe', @t, @t, @t)";
            cmd.Parameters.AddWithValue("@u", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@n", probeUsername);
            cmd.Parameters.AddWithValue("@t", now);
            cmd.ExecuteNonQuery();
        });

        using var second = new SqliteConnectionProvider(Settings);
        var fetched = await second.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Username FROM Users WHERE Username = @n";
            cmd.Parameters.AddWithValue("@n", probeUsername);
            return (string?)cmd.ExecuteScalar();
        });

        Assert.AreEqual(probeUsername, fetched);
    }

    [TestMethod]
    public async Task ConnectionProvider_ExistingRollbackJournalDatabase_ConvertsToWalKeepingData()
    {
        var legacyDir = Path.Combine(TempDir, "legacy");
        Directory.CreateDirectory(legacyDir);
        var legacySettings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = legacyDir,
            EbookFolder = Path.Combine(legacyDir, "ebooks"),
            AudiobookFolder = Path.Combine(legacyDir, "audiobooks")
        };

        using (var legacyConn = new SqliteConnection($"Data Source={legacySettings.Database};Pooling=False"))
        {
            legacyConn.Open();
            using var cmd = legacyConn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Probe (Value TEXT); INSERT INTO Probe (Value) VALUES ('kept')";
            cmd.ExecuteNonQuery();
        }

        using var provider = new SqliteConnectionProvider(legacySettings);
        var (journalMode, value) = await provider.Read(conn =>
        {
            using var journalCmd = conn.CreateCommand();
            journalCmd.CommandText = "PRAGMA journal_mode";
            var mode = (string)journalCmd.ExecuteScalar()!;

            using var valueCmd = conn.CreateCommand();
            valueCmd.CommandText = "SELECT Value FROM Probe";
            return (mode, (string?)valueCmd.ExecuteScalar());
        });

        Assert.AreEqual("wal", journalMode);
        Assert.AreEqual("kept", value);
    }

    /// <summary>
    /// Regression guard: proves the connection layer below <see cref="SqliteConnectionProvider"/>
    /// honours transaction rollback. If the provider is ever swapped for a backend that doesn't
    /// (or for one that auto-commits on every statement), this test fails — which is exactly the
    /// signal we want since CollectionRepository.Upsert / Delete rely on transactional atomicity.
    /// </summary>
    [TestMethod]
    public async Task Transaction_Rollback_DiscardsWritesWithinTransaction()
    {
        var countBefore = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        await ConnectionProvider.Write(conn =>
        {
            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var now = DateTimeOffset.UtcNow.ToString("O");
            cmd.CommandText = "INSERT INTO Users (UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt) VALUES (@u, @n, 'fake:x', 'Rolled Back', @t, @t, @t)";
            cmd.Parameters.AddWithValue("@u", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@n", $"rollback-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("@t", now);
            cmd.ExecuteNonQuery();

            tx.Rollback();
        });

        var countAfter = await ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        Assert.AreEqual(countBefore, countAfter);
    }
}
