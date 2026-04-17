using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class ReadingStateRepositoryTests : DatabaseTestBase
{
    private ReadingStateRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new ReadingStateRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task ReadingStateRepository_GetByBookUuid_Missing_ReturnsNull()
    {
        Assert.IsNull(await repository.GetByBookUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ReadingStateRepository_Upsert_Insert_RoundTripsFields()
    {
        var bookUuid = Guid.NewGuid();
        var state = BuildState(status: "Reading", progress: 42);

        await repository.Upsert(bookUuid, state);

        var fetched = await repository.GetByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Reading", fetched.StatusInfo.Status);
        Assert.AreEqual(42, Convert.ToInt32(fetched.CurrentBookmark.ProgressPercent));
    }

    [TestMethod]
    public async Task ReadingStateRepository_Upsert_Existing_UpdatesInPlace()
    {
        var bookUuid = Guid.NewGuid();
        await repository.Upsert(bookUuid, BuildState(status: "Reading", progress: 10));

        await repository.Upsert(bookUuid, BuildState(status: "Finished", progress: 100));

        var fetched = await repository.GetByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Finished", fetched.StatusInfo.Status);
        Assert.AreEqual(100, Convert.ToInt32(fetched.CurrentBookmark.ProgressPercent));
    }

    [TestMethod]
    public async Task ReadingStateRepository_GetReadingProgress_ReturnsTypedProjection()
    {
        var bookUuid = Guid.NewGuid();
        await repository.Upsert(bookUuid, BuildState(status: "Finished", progress: 100, spentMinutes: 45));

        var progress = await repository.GetReadingProgress(bookUuid);

        Assert.IsNotNull(progress);
        Assert.AreEqual(ReadingStatus.Finished, progress.Status);
        Assert.AreEqual(100m, progress.ProgressPercent);
        Assert.AreEqual(45, progress.SpentReadingMinutes);
    }

    [TestMethod]
    public async Task ReadingStateRepository_GetReadingProgress_MissingRow_ReturnsNull()
    {
        Assert.IsNull(await repository.GetReadingProgress(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ReadingStateRepository_Upsert_PersistsBookmarkLocation()
    {
        var bookUuid = Guid.NewGuid();
        var state = BuildState(status: "Reading", progress: 50);
        state.CurrentBookmark.Location = new KoboStoreBookmarkLocation
        {
            Value = "some-location",
            Type = "KoboSpan",
            Source = "Tolino"
        };

        await repository.Upsert(bookUuid, state);

        var fetched = await repository.GetByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.IsNotNull(fetched.CurrentBookmark.Location);
        Assert.AreEqual("some-location", fetched.CurrentBookmark.Location.Value);
        Assert.AreEqual("KoboSpan", fetched.CurrentBookmark.Location.Type);
        Assert.AreEqual("Tolino", fetched.CurrentBookmark.Location.Source);
    }

    private static KoboStoreReadingState BuildState(string status = "ReadyToRead", int progress = 0, int? spentMinutes = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new KoboStoreReadingState
        {
            EntitlementId = Guid.NewGuid().ToString(),
            Created = now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo
            {
                LastModified = now,
                Status = status,
                TimesStartedReading = status == "Reading" ? 1 : 0,
                LastTimeStartedReading = status == "Reading" ? now : null
            },
            Statistics = new KoboStoreStatistics
            {
                LastModified = now,
                SpentReadingMinutes = spentMinutes,
                RemainingTimeMinutes = null
            },
            CurrentBookmark = new KoboStoreBookmark
            {
                LastModified = now,
                ProgressPercent = progress,
                ContentSourceProgressPercent = progress,
                Location = null
            }
        };
    }
}
