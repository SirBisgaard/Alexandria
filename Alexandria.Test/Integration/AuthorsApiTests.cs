using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class AuthorsApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/authors ----------

    [TestMethod]
    public async Task List_NoAuthors_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/authors");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthorDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_AuthorsWithoutBooks_AreFilteredOut()
    {
        // An author that exists but has no books should not appear — the service filters them.
        var authorRepo = GetService<IAuthorRepository>();
        await authorRepo.GetOrCreate("Lonely Author");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/authors");

        var body = await response.Content.ReadFromJsonAsync<AuthorDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_WithBooks_ReturnsPopulatedAuthors()
    {
        await SeedBookByAuthor("First", "Alice Author");
        await SeedBookByAuthor("Second", "Alice Author");
        await SeedBookByAuthor("Third", "Bob Author");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/authors");

        var body = await response.Content.ReadFromJsonAsync<AuthorDto[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(2, body);
        var alice = body.Single(a => a.Name == "Alice Author");
        var bob = body.Single(a => a.Name == "Bob Author");
        Assert.HasCount(2, alice.Books);
        Assert.HasCount(1, bob.Books);
    }

    [TestMethod]
    public async Task List_SortByNumberOfBooksDesc_OrdersProlificFirst()
    {
        await SeedBookByAuthor("Book1", "Prolific");
        await SeedBookByAuthor("Book2", "Prolific");
        await SeedBookByAuthor("Book3", "Prolific");
        await SeedBookByAuthor("Book4", "Sparse");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/authors?sort=NumberOfBooks&order=Desc");

        var body = await response.Content.ReadFromJsonAsync<AuthorDto[]>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Prolific", body[0].Name);
        Assert.AreEqual("Sparse", body[1].Name);
    }

    // ---------- GET /api/alexandria/authors/{uuid} ----------

    [TestMethod]
    public async Task Get_Existing_Returns200WithBooksSortedByTitleAsc()
    {
        await SeedBookByAuthor("Zeta", "Target Author");
        await SeedBookByAuthor("Alpha", "Target Author");
        var author = await FindAuthorByName("Target Author");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/authors/{author.AuthorUuid}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthorDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Target Author", body.Name);
        Assert.HasCount(2, body.Books);
        Assert.AreEqual("Alpha", body.Books[0].Title);
        Assert.AreEqual("Zeta", body.Books[1].Title);
    }

    [TestMethod]
    public async Task Get_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/authors/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/authors/{uuid} ----------

    [TestMethod]
    public async Task Put_Rename_Returns200WithUpdatedName()
    {
        // Rename triggers reindex-per-owned-book. Seeding a bookless author keeps the reindex set
        // empty so the HTTP-level test doesn't need a real EPUB on disk. Business tests cover the
        // reindex loop itself.
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Old Name");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/authors/{authorUuid}", new { name = "New Name" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthorDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("New Name", body.Name);
    }

    [TestMethod]
    public async Task Put_RenameToExistingAuthor_MergesAndReturnsTarget()
    {
        // Target owns a book so it survives the post-merge RemoveOrphanedAuthors cleanup.
        // Source stays bookless so the reindex loop skips real source files.
        await SeedBookByAuthor("Target's Book", "Target Author");
        var authorRepo = GetService<IAuthorRepository>();
        var sourceUuid = await authorRepo.GetOrCreate("Source Author");
        var target = (await authorRepo.FindByName("Target Author"))!;

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/authors/{sourceUuid}", new { name = "Target Author" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthorDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(target.AuthorUuid, body.AuthorUuid);
        Assert.AreEqual("Target Author", body.Name);
        Assert.HasCount(1, body.Books);
    }

    [TestMethod]
    public async Task Put_WhitespaceOnlyName_Returns400()
    {
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Original");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/authors/{authorUuid}", new { name = "   " });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_EmptyName_Returns400()
    {
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Original");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/authors/{authorUuid}", new { name = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/authors/{Guid.NewGuid()}", new { name = "Ghost" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task SeedBookByAuthor(string title, string authorName)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();

        var authorUuid = await authorRepo.GetOrCreate(authorName);
        var bookUuid = Guid.NewGuid();
        await bookRepo.Add(new Book
        {
            BookUuid = bookUuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = authorName }],
            File = new BookFile { BookUuid = bookUuid, MediaType = MediaType.Ebook, SourcePath = $"/tmp/{bookUuid}.epub" }
        });
        await authorRepo.SetBookAuthors(bookUuid, [authorName]);
    }

    private async Task<Author> FindAuthorByName(string name)
    {
        var repo = GetService<IAuthorRepository>();
        return (await repo.FindByName(name))!;
    }

    private sealed class AuthorDto
    {
        public Guid AuthorUuid { get; set; }
        public string Name { get; set; } = "";
        public List<BookSummaryDto> Books { get; set; } = [];
    }

    private sealed class BookSummaryDto
    {
        public Guid BookUuid { get; set; }
        public string Title { get; set; } = "";
    }
}
