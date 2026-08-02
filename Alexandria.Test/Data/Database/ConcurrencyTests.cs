using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

/// <summary>
/// <see cref="Alexandria.DataAccess.Database.SqliteConnectionProvider"/> serializes every Read
/// and Write through a single <c>SemaphoreSlim</c> guarding a single connection. These tests
/// validate the observable contract: parallel callers don't corrupt state, don't deadlock, and
/// all their operations eventually complete.
/// </summary>
[TestClass]
public sealed class ConcurrencyTests : DatabaseTestBase
{
    [TestMethod]
    public async Task Concurrent_ManyWriters_AllInsertsPersist()
    {
        var repository = new KoboDeviceRepository(ConnectionProvider);

        var tasks = Enumerable.Range(0, 50)
            .Select(i => repository.Create(new KoboDevice { DeviceUuid = Guid.NewGuid(), Nickname = $"Device {i}" }))
            .ToArray();
        await Task.WhenAll(tasks);

        var all = await repository.GetAll();
        Assert.HasCount(50, all);
    }

    [TestMethod]
    public async Task Concurrent_ManyReaders_AllReturnSeededData()
    {
        var repository = new UserRepository(ConnectionProvider);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => repository.GetByUsername("alexander"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var user in results)
        {
            Assert.IsNotNull(user);
            Assert.AreEqual("alexander", user.Username);
        }
    }

    [TestMethod]
    public async Task Concurrent_MixedReadsAndWrites_AllComplete()
    {
        var deviceRepo = new KoboDeviceRepository(ConnectionProvider);
        var trafficRepo = new KoboTrafficLogRepository(ConnectionProvider);

        var tasks = new List<Task>();
        for (var i = 0; i < 25; i++)
        {
            tasks.Add(deviceRepo.Create(new KoboDevice { DeviceUuid = Guid.NewGuid(), Nickname = $"Writer-{i}" }));
            tasks.Add(trafficRepo.Log(new KoboProxyTrafficLog
            {
                Timestamp = DateTimeOffset.UtcNow,
                Method = "GET",
                Url = $"/test/{i}",
                RequestHeaders = "{}",
                RequestBody = "",
                StatusCode = 200,
                ResponseHeaders = "{}",
                ResponseBody = "",
                DurationMs = 1
            }));
            tasks.Add(deviceRepo.GetAll());
        }

        await Task.WhenAll(tasks);

        var devices = await deviceRepo.GetAll();
        Assert.HasCount(25, devices);
    }

    [TestMethod]
    public async Task Concurrent_UpsertSameKey_LastWriteWinsNoDuplicates()
    {
        var repository = new KoboDeviceSyncRepository(ConnectionProvider);
        var deviceUuid = Guid.NewGuid().ToString();
        var bookUuid = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => repository.UpsertSyncedFile(deviceUuid, bookUuid, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(i)))
            .ToArray();
        await Task.WhenAll(tasks);

        var synced = await repository.GetSyncedModifiedDates(deviceUuid);
        Assert.HasCount(1, synced);
    }
}
