using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class AuthorRepositoryTests : DatabaseTestBase
{
    private AuthorRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new AuthorRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task AuthorRepository_GetOrCreate_NewName_CreatesRow()
    {
        var uuid = await repository.GetOrCreate("Stephen King");

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Stephen King", fetched.Name);
    }

    [TestMethod]
    public async Task AuthorRepository_GetOrCreate_ExistingName_ReturnsSameUuid()
    {
        var first = await repository.GetOrCreate("George R. R. Martin");
        var second = await repository.GetOrCreate("George R. R. Martin");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task AuthorRepository_GetOrCreate_CaseInsensitiveNameMatch()
    {
        var lower = await repository.GetOrCreate("terry pratchett");
        var upper = await repository.GetOrCreate("Terry Pratchett");

        Assert.AreEqual(lower, upper);
        Assert.HasCount(1, await repository.GetAll());
    }

    [TestMethod]
    public async Task AuthorRepository_FindByName_CaseInsensitiveMatch()
    {
        await repository.GetOrCreate("Neil Gaiman");

        var fetched = await repository.FindByName("NEIL GAIMAN");

        Assert.IsNotNull(fetched);
        Assert.AreEqual("Neil Gaiman", fetched.Name);
    }

    [TestMethod]
    public async Task AuthorRepository_FindByName_Unknown_ReturnsNull()
    {
        Assert.IsNull(await repository.FindByName("no one"));
    }

    [TestMethod]
    public async Task AuthorRepository_GetAll_ReturnsRowsSortedByName()
    {
        await repository.GetOrCreate("Cormac McCarthy");
        await repository.GetOrCreate("Alice Munro");
        await repository.GetOrCreate("Brandon Sanderson");

        var names = (await repository.GetAll()).Select(a => a.Name).ToList();

        Assert.AreEqual("Alice Munro", names[0]);
        Assert.AreEqual("Brandon Sanderson", names[1]);
        Assert.AreEqual("Cormac McCarthy", names[2]);
    }

    [TestMethod]
    public async Task AuthorRepository_Update_ModifiesName()
    {
        var uuid = await repository.GetOrCreate("Original Name");

        await repository.Update(new Author { AuthorUuid = uuid, Name = "Renamed" });

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Renamed", fetched.Name);
    }

    [TestMethod]
    public async Task AuthorRepository_SetBookAuthors_CreatesMissingAuthorsAndLinks()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);

        await repository.SetBookAuthors(bookUuid, ["First Author", "Second Author"]);

        var linked = await repository.GetAuthorsByBookUuid(bookUuid);
        Assert.HasCount(2, linked);
        Assert.Contains("First Author", linked.Select(a => a.Name));
        Assert.Contains("Second Author", linked.Select(a => a.Name));
    }

    [TestMethod]
    public async Task AuthorRepository_SetBookAuthors_ReplacesExistingLinks()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookAuthors(bookUuid, ["Old A", "Old B"]);

        await repository.SetBookAuthors(bookUuid, ["New A"]);

        var linked = await repository.GetAuthorsByBookUuid(bookUuid);
        Assert.HasCount(1, linked);
        Assert.AreEqual("New A", linked[0].Name);
    }

    [TestMethod]
    public async Task AuthorRepository_RemoveOrphanedAuthors_RemovesAuthorsWithNoBook()
    {
        var orphaned = await repository.GetOrCreate("Orphan");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookAuthors(bookUuid, ["Linked Author"]);

        await repository.RemoveOrphanedAuthors();

        Assert.IsNull(await repository.FindByUuid(orphaned));
        Assert.IsNotNull(await repository.FindByName("Linked Author"));
    }

    [TestMethod]
    public async Task AuthorRepository_ReassignBooks_MovesBookLinksFromSourceToTarget()
    {
        var sourceUuid = await repository.GetOrCreate("Source Author");
        var targetUuid = await repository.GetOrCreate("Target Author");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookAuthors(bookUuid, ["Source Author"]);

        await repository.ReassignBooks(sourceUuid, targetUuid);

        var linked = await repository.GetAuthorsByBookUuid(bookUuid);
        Assert.HasCount(1, linked);
        Assert.AreEqual("Target Author", linked[0].Name);
    }

    [TestMethod]
    public async Task AuthorRepository_ReassignBooks_WhenTargetAlreadyLinked_DropsSourceLinkWithoutDuplicate()
    {
        var sourceUuid = await repository.GetOrCreate("Source Author");
        var targetUuid = await repository.GetOrCreate("Target Author");
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        await repository.SetBookAuthors(bookUuid, ["Source Author", "Target Author"]);

        await repository.ReassignBooks(sourceUuid, targetUuid);

        var linked = await repository.GetAuthorsByBookUuid(bookUuid);
        Assert.HasCount(1, linked);
        Assert.AreEqual("Target Author", linked[0].Name);
    }
}
