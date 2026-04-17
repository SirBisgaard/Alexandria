using Alexandria.DataAccess.Database;
using Microsoft.Data.Sqlite;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class SqliteConnectionProviderTests : DatabaseTestBase
{
    [TestMethod]
    public async Task Flush_WritesInMemoryStateToDiskFile()
    {
        Assert.IsFalse(File.Exists(Settings.Database), "Disk file should not exist before the first Flush.");

        var result = await ConnectionProvider.Flush();

        Assert.IsTrue(result);
        Assert.IsTrue(File.Exists(Settings.Database));

        using var diskConn = new SqliteConnection($"Data Source={Settings.Database}");
        diskConn.Open();
        using var cmd = diskConn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'alexander'";
        Assert.AreEqual(1, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    [TestMethod]
    public async Task Flush_ConsecutiveCallsWithoutActivity_SecondReturnsFalse()
    {
        var first = await ConnectionProvider.Flush();
        var second = await ConnectionProvider.Flush();

        Assert.IsTrue(first, "First flush after initialization must perform the backup.");
        Assert.IsFalse(second, "Second consecutive flush must short-circuit because nothing happened in between.");
    }

    [TestMethod]
    public async Task Flush_AfterReadSinceLastFlush_RunsAgain()
    {
        await ConnectionProvider.Flush();
        await ConnectionProvider.Read(_ => 0);

        var result = await ConnectionProvider.Flush();

        Assert.IsTrue(result, "A Read between flushes resets the short-circuit flag, so the next Flush runs.");
    }

    [TestMethod]
    public async Task ConnectionProvider_Constructor_WithExistingDiskFile_RestoresState()
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
        await ConnectionProvider.Flush();

        using var restored = new SqliteConnectionProvider(Settings);
        var fetched = await restored.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Username FROM Users WHERE Username = @n";
            cmd.Parameters.AddWithValue("@n", probeUsername);
            return (string?)cmd.ExecuteScalar();
        });

        Assert.AreEqual(probeUsername, fetched);
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
