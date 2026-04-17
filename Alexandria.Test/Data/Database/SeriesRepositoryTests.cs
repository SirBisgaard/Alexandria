using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class SeriesRepositoryTests : DatabaseTestBase
{
    private SeriesRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new SeriesRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task SeriesRepository_GetOrCreate_NewName_CreatesRow()
    {
        var uuid = await repository.GetOrCreate("A Song of Ice and Fire");

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("A Song of Ice and Fire", fetched.Name);
    }

    [TestMethod]
    public async Task SeriesRepository_GetOrCreate_ExistingName_ReturnsSameUuid()
    {
        var first = await repository.GetOrCreate("The Witcher");
        var second = await repository.GetOrCreate("The Witcher");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task SeriesRepository_GetOrCreate_CaseInsensitive()
    {
        var a = await repository.GetOrCreate("mistborn");
        var b = await repository.GetOrCreate("Mistborn");

        Assert.AreEqual(a, b);
        Assert.HasCount(1, await repository.GetAll());
    }

    [TestMethod]
    public async Task SeriesRepository_FindByName_CaseInsensitive()
    {
        await repository.GetOrCreate("The Expanse");

        var fetched = await repository.FindByName("THE EXPANSE");

        Assert.IsNotNull(fetched);
        Assert.AreEqual("The Expanse", fetched.Name);
    }

    [TestMethod]
    public async Task SeriesRepository_GetAll_ReturnsRowsSortedByName()
    {
        await repository.GetOrCreate("Culture");
        await repository.GetOrCreate("Alexandria");
        await repository.GetOrCreate("Bobiverse");

        var names = (await repository.GetAll()).Select(s => s.Name).ToList();

        Assert.AreEqual("Alexandria", names[0]);
        Assert.AreEqual("Bobiverse", names[1]);
        Assert.AreEqual("Culture", names[2]);
    }

    [TestMethod]
    public async Task SeriesRepository_Update_ModifiesName()
    {
        var uuid = await repository.GetOrCreate("Old Name");

        await repository.Update(new Series { SeriesUuid = uuid, Name = "New Name" });

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("New Name", fetched.Name);
    }

    [TestMethod]
    public async Task SeriesRepository_SetBookSeries_PersistsIndex()
    {
        var seriesUuid = await repository.GetOrCreate("Discworld");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);

        await repository.SetBookSeries(bookUuid, seriesUuid, 5m);

        var fetched = await repository.GetSeriesByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Discworld", fetched.Name);
        Assert.AreEqual(5m, fetched.Index);
    }

    [TestMethod]
    public async Task SeriesRepository_SetBookSeries_ReplacesExistingLink()
    {
        var firstSeries = await repository.GetOrCreate("First Series");
        var secondSeries = await repository.GetOrCreate("Second Series");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookSeries(bookUuid, firstSeries, 1m);

        await repository.SetBookSeries(bookUuid, secondSeries, 2m);

        var fetched = await repository.GetSeriesByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Second Series", fetched.Name);
        Assert.AreEqual(2m, fetched.Index);
    }

    [TestMethod]
    public async Task SeriesRepository_RemoveBookSeries_DetachesBookFromSeries()
    {
        var seriesUuid = await repository.GetOrCreate("Ephemeral Series");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookSeries(bookUuid, seriesUuid, 1m);

        await repository.RemoveBookSeries(bookUuid);

        Assert.IsNull(await repository.GetSeriesByBookUuid(bookUuid));
    }

    [TestMethod]
    public async Task SeriesRepository_RemoveOrphanedSeries_RemovesSeriesWithNoBook()
    {
        var orphaned = await repository.GetOrCreate("Orphan Series");
        var used = await repository.GetOrCreate("Used Series");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookSeries(bookUuid, used, 1m);

        await repository.RemoveOrphanedSeries();

        Assert.IsNull(await repository.FindByUuid(orphaned));
        Assert.IsNotNull(await repository.FindByUuid(used));
    }

    [TestMethod]
    public async Task SeriesRepository_ReassignBooks_MovesBookLinksFromSourceToTarget()
    {
        var source = await repository.GetOrCreate("Source");
        var target = await repository.GetOrCreate("Target");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookSeries(bookUuid, source, 3m);

        await repository.ReassignBooks(source, target);

        var fetched = await repository.GetSeriesByBookUuid(bookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Target", fetched.Name);
    }
}
