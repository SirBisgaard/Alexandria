using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class KoboTrafficLogRepositoryTests : DatabaseTestBase
{
    private KoboTrafficLogRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new KoboTrafficLogRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task KoboTrafficLogRepository_Log_WritesRow()
    {
        await repository.Log(BuildEntry());

        var count = await CountRows();
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task KoboTrafficLogRepository_Log_PersistsFieldsRoundTrip()
    {
        var entry = BuildEntry(method: "POST", url: "/v1/library/sync", statusCode: 200, durationMs: 42);

        await repository.Log(entry);

        var fetched = await ReadFirstRow();
        Assert.AreEqual("POST", fetched.Method);
        Assert.AreEqual("/v1/library/sync", fetched.Url);
        Assert.AreEqual(200, fetched.StatusCode);
        Assert.AreEqual(42, fetched.DurationMs);
    }

    [TestMethod]
    public async Task KoboTrafficLogRepository_Log_MultipleEntries_AllPersisted()
    {
        await repository.Log(BuildEntry(method: "GET"));
        await repository.Log(BuildEntry(method: "POST"));
        await repository.Log(BuildEntry(method: "PUT"));

        Assert.AreEqual(3, await CountRows());
    }

    [TestMethod]
    public async Task KoboTrafficLogRepository_Clear_DeletesAllRows()
    {
        await repository.Log(BuildEntry());
        await repository.Log(BuildEntry());

        await repository.Clear();

        Assert.AreEqual(0, await CountRows());
    }

    [TestMethod]
    public async Task KoboTrafficLogRepository_Clear_OnEmptyTable_Succeeds()
    {
        await repository.Clear();

        Assert.AreEqual(0, await CountRows());
    }

    private static KoboProxyTrafficLog BuildEntry(string method = "GET", string url = "/test", int statusCode = 200, long durationMs = 10) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Method = method,
        Url = url,
        RequestHeaders = "{}",
        RequestBody = "",
        StatusCode = statusCode,
        ResponseHeaders = "{}",
        ResponseBody = "",
        DurationMs = durationMs
    };

    private Task<int> CountRows() =>
        ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM KoboTrafficLogs";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

    private Task<KoboProxyTrafficLog> ReadFirstRow() =>
        ConnectionProvider.Read(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Method, Url, StatusCode, DurationMs FROM KoboTrafficLogs ORDER BY Id LIMIT 1";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return new KoboProxyTrafficLog
            {
                Method = reader.GetString(0),
                Url = reader.GetString(1),
                StatusCode = reader.GetInt32(2),
                DurationMs = reader.GetInt64(3),
                RequestHeaders = "",
                RequestBody = "",
                ResponseHeaders = "",
                ResponseBody = ""
            };
        });
}
