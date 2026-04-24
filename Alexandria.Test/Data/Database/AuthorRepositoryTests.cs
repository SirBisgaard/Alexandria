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
        var uuid = await repository.GetOrCreate("Sample Author");

        var fetched = await repository.FindByUuid(uuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Sample Author", fetched.Name);
    }

    [TestMethod]
    public async Task AuthorRepository_GetOrCreate_ExistingName_ReturnsSameUuid()
    {
        var first = await repository.GetOrCreate("Repeat Author");
        var second = await repository.GetOrCreate("Repeat Author");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task AuthorRepository_GetOrCreate_CaseInsensitiveNameMatch()
    {
        var lower = await repository.GetOrCreate("mixed case author");
        var upper = await repository.GetOrCreate("Mixed Case Author");

        Assert.AreEqual(lower, upper);
        Assert.HasCount(1, await repository.GetAll());
    }

    [TestMethod]
    public async Task AuthorRepository_FindByName_CaseInsensitiveMatch()
    {
        await repository.GetOrCreate("Case Insensitive");

        var fetched = await repository.FindByName("CASE INSENSITIVE");

        Assert.IsNotNull(fetched);
        Assert.AreEqual("Case Insensitive", fetched.Name);
    }

    [TestMethod]
    public async Task AuthorRepository_FindByName_Unknown_ReturnsNull()
    {
        Assert.IsNull(await repository.FindByName("no one"));
    }

    [TestMethod]
    public async Task AuthorRepository_GetAll_ReturnsRowsSortedByName()
    {
        await repository.GetOrCreate("Charlie Author");
        await repository.GetOrCreate("Alpha Author");
        await repository.GetOrCreate("Bravo Author");

        var names = (await repository.GetAll()).Select(a => a.Name).ToList();

        Assert.AreEqual("Alpha Author", names[0]);
        Assert.AreEqual("Bravo Author", names[1]);
        Assert.AreEqual("Charlie Author", names[2]);
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
