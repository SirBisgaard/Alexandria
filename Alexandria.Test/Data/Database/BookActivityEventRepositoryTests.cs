using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class BookActivityEventRepositoryTests : DatabaseTestBase
{
    private BookActivityEventRepository repository = null!;

    [TestInitialize]
    public void SetupRepo() => repository = new BookActivityEventRepository(ConnectionProvider);

    [TestMethod]
    public async Task GetLatest_NoEvents_ReturnsNull()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);

        Assert.IsNull(await repository.GetLatest(bookUuid));
    }

    [TestMethod]
    public async Task GetLatest_MultipleDevices_ReturnsNewestByEventTimestamp()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        var now = DateTimeOffset.UtcNow;
        await repository.Insert(BuildEvent(bookUuid, "abs", "AudioBookShelfIntegration", now.AddMinutes(-10)));
        await repository.Insert(BuildEvent(bookUuid, "kobo", "kobo-device", now.AddMinutes(-5)));

        var latest = await repository.GetLatest(bookUuid);

        Assert.IsNotNull(latest);
        Assert.AreEqual("kobo", latest.EventId);
    }

    [TestMethod]
    public async Task UpdateTimestampAndBody_KeepsReceivedAtAndEventId()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        var start = DateTimeOffset.UtcNow.AddMinutes(-20);
        await repository.Insert(BuildEvent(bookUuid, "session", "AudioBookShelfIntegration", start));
        var end = start.AddMinutes(20);

        await repository.UpdateTimestampAndBody("session", end, "updated");

        var stored = Assert.ContainsSingle(await repository.GetByBookUuid(bookUuid));
        Assert.AreEqual("session", stored.EventId);
        Assert.AreEqual(end, stored.EventTimestamp);
        Assert.AreEqual(start, stored.ReceivedAt);
        Assert.AreEqual("updated", stored.Body);
    }

    private static BookActivityEvent BuildEvent(Guid bookUuid, string eventId, string deviceUuid, DateTimeOffset timestamp) => new()
    {
        EventId = eventId,
        BookUuid = bookUuid,
        DeviceUuid = deviceUuid,
        EventType = "AbsProgressUpdated",
        EventTimestamp = timestamp,
        Body = "body",
        ReceivedAt = timestamp
    };
}
