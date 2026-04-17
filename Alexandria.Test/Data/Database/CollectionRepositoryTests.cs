using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class CollectionRepositoryTests : DatabaseTestBase
{
    private CollectionRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new CollectionRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task CollectionRepository_Upsert_NewCollection_PersistsRow()
    {
        var collection = BuildCollection("My Shelf");

        await repository.Upsert(collection);

        var fetched = await repository.FindByUuid(collection.CollectionUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("My Shelf", fetched.Name);
    }

    [TestMethod]
    public async Task CollectionRepository_Upsert_Existing_ReplacesNameAndModifiedAt()
    {
        var uuid = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.Upsert(new BookCollection { CollectionUuid = uuid, Name = "Old Name", CreatedAt = createdAt, ModifiedAt = createdAt });

        var laterModified = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await repository.Upsert(new BookCollection { CollectionUuid = uuid, Name = "Renamed", CreatedAt = createdAt, ModifiedAt = laterModified });

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Renamed", fetched.Name);
        Assert.AreEqual(laterModified, fetched.ModifiedAt);
    }

    [TestMethod]
    public async Task CollectionRepository_Upsert_WithBookUuids_PersistsMembership()
    {
        var book1 = Guid.NewGuid();
        var book2 = Guid.NewGuid();
        await InsertBookShell(book1);
        await InsertBookShell(book2);

        var collection = BuildCollection("Favorites", [book1, book2]);
        await repository.Upsert(collection);

        var fetched = await repository.FindByUuid(collection.CollectionUuid);
        Assert.IsNotNull(fetched);
        Assert.HasCount(2, fetched.BookUuids);
        Assert.Contains(book1, fetched.BookUuids);
        Assert.Contains(book2, fetched.BookUuids);
    }

    [TestMethod]
    public async Task CollectionRepository_Upsert_ReplacesBookUuids()
    {
        var oldBook = Guid.NewGuid();
        var newBook = Guid.NewGuid();
        await InsertBookShell(oldBook);
        await InsertBookShell(newBook);

        var collection = BuildCollection("Shelf", [oldBook]);
        await repository.Upsert(collection);

        collection.BookUuids = [newBook];
        await repository.Upsert(collection);

        var fetched = await repository.FindByUuid(collection.CollectionUuid);
        Assert.IsNotNull(fetched);
        Assert.HasCount(1, fetched.BookUuids);
        Assert.AreEqual(newBook, fetched.BookUuids[0]);
    }

    [TestMethod]
    public async Task CollectionRepository_FindByUuid_Missing_ReturnsNull()
    {
        Assert.IsNull(await repository.FindByUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task CollectionRepository_GetAll_ReturnsAllCollections()
    {
        await repository.Upsert(BuildCollection("A"));
        await repository.Upsert(BuildCollection("B"));
        await repository.Upsert(BuildCollection("C"));

        var all = await repository.GetAll();

        Assert.HasCount(3, all);
    }

    [TestMethod]
    public async Task CollectionRepository_Delete_RemovesCollectionAndBookLinks()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        var collection = BuildCollection("Doomed", [bookUuid]);
        await repository.Upsert(collection);

        await repository.Delete(collection.CollectionUuid);

        Assert.IsNull(await repository.FindByUuid(collection.CollectionUuid));
        Assert.IsEmpty(await repository.GetAll());
    }

    [TestMethod]
    public async Task CollectionRepository_GetModifiedSince_ReturnsOnlyRecentlyModified()
    {
        var cutoff = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var older = new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "Old", CreatedAt = cutoff.AddDays(-10), ModifiedAt = cutoff.AddDays(-10) };
        var newer = new BookCollection { CollectionUuid = Guid.NewGuid(), Name = "New", CreatedAt = cutoff.AddDays(-10), ModifiedAt = cutoff.AddDays(10) };
        await repository.Upsert(older);
        await repository.Upsert(newer);

        var fetched = await repository.GetModifiedSince(cutoff);

        Assert.HasCount(1, fetched);
        Assert.AreEqual("New", fetched[0].Name);
    }

    private static BookCollection BuildCollection(string name, List<Guid>? bookUuids = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new BookCollection
        {
            CollectionUuid = Guid.NewGuid(),
            Name = name,
            BookUuids = bookUuids ?? [],
            CreatedAt = now,
            ModifiedAt = now
        };
    }
}
