using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class BooksApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/books ----------

    [TestMethod]
    public async Task List_NoBooks_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/books");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_WithBooks_ReturnsAll()
    {
        await SeedShellBook("Alpha");
        await SeedShellBook("Beta");
        await SeedShellBook("Gamma");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/books");

        var body = await response.Content.ReadFromJsonAsync<BookDto[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(3, body);
    }

    [TestMethod]
    public async Task List_SortByTitleAsc_OrdersAlphabetically()
    {
        await SeedShellBook("Zeta");
        await SeedShellBook("Alpha");
        await SeedShellBook("Mu");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/books?sort=Title&order=Asc");

        var body = await response.Content.ReadFromJsonAsync<BookDto[]>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Alpha", body[0].Title);
        Assert.AreEqual("Mu", body[1].Title);
        Assert.AreEqual("Zeta", body[2].Title);
    }

    // ---------- GET /api/alexandria/books/{uuid} ----------

    [TestMethod]
    public async Task Get_ExistingUuid_Returns200WithBookResponse()
    {
        var book = await SeedShellBook("Target");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/books/{book.BookUuid}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BookDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(book.BookUuid, body.BookUuid);
        Assert.AreEqual("Target", body.Title);
    }

    [TestMethod]
    public async Task Get_UnknownUuid_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/books/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/books/{uuid}/status ----------

    [TestMethod]
    public async Task PutStatus_Reading_Returns200AndPersistsStateViaSubsequentGet()
    {
        var book = await SeedShellBook("For Reading");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/status", new { status = "Reading" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await authed.GetAsync($"/api/alexandria/books/{book.BookUuid}");
        var body = await getResponse.Content.ReadFromJsonAsync<BookDto>();
        Assert.IsNotNull(body);
        Assert.IsNotNull(body.ReadingProgress);
        Assert.AreEqual("Reading", body.ReadingProgress.Status);
    }

    // ---------- PUT /api/alexandria/books/{uuid}/archive ----------

    [TestMethod]
    public async Task PutArchive_True_Returns200AndSetsIsArchived()
    {
        var book = await SeedShellBook("Doomed");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/archive", new { isArchived = true });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await authed.GetAsync($"/api/alexandria/books/{book.BookUuid}");
        var body = await getResponse.Content.ReadFromJsonAsync<BookDto>();
        Assert.IsNotNull(body);
        Assert.IsTrue(body.IsArchived);
    }

    [TestMethod]
    public async Task PutArchive_UnknownUuid_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{Guid.NewGuid()}/archive", new { isArchived = true });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- GET /api/alexandria/books/{uuid}/cover ----------

    [TestMethod]
    public async Task GetCover_UnknownBook_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/books/{Guid.NewGuid()}/cover");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetCover_ExistingBookWithCover_ReturnsJpegContent()
    {
        var coverPath = Fixtures.CopyCoverTo(Path.Combine(TempDir, "data", "covers"));
        var book = await SeedShellBook("WithCover", coverFilePath: coverPath);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/books/{book.BookUuid}/cover");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.IsGreaterThan(100, bytes.Length);
        Assert.AreEqual(0xFF, bytes[0]);
        Assert.AreEqual(0xD8, bytes[1]);
    }

    [TestMethod]
    public async Task GetCover_BookWithNoCoverFile_Returns404()
    {
        var book = await SeedShellBook("Coverless");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/books/{book.BookUuid}/cover");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/books/{uuid}/metadata ----------

    [TestMethod]
    public async Task PutMetadata_MissingTitle_Returns400()
    {
        var book = await SeedShellBook("With Title");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "",
            authors = new[] { "Default Author" }
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutMetadata_NoAuthors_Returns400()
    {
        var book = await SeedShellBook("Present");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "Title",
            authors = Array.Empty<string>()
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutMetadata_InvalidPublicationDate_Returns400()
    {
        var book = await SeedShellBook("Present");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "Title",
            authors = new[] { "Author" },
            publicationDate = "not-a-date"
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutMetadata_AudiobookWithNonNarratorContributor_Returns400()
    {
        var book = await SeedShellBook("Audiobook", mediaType: MediaType.Audiobook);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "Title",
            authors = new[] { "Author" },
            contributors = new[] { new { name = "Editor Person", role = "Editor" } }
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutMetadata_UnknownUuid_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{Guid.NewGuid()}/metadata", new
        {
            title = "Title",
            authors = new[] { "Author" }
        });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/books/{uuid}/cover ----------

    [TestMethod]
    public async Task PutCover_NoFile_Returns400()
    {
        var book = await SeedShellBook("Target");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var formContent = new MultipartFormDataContent();

        var response = await authed.PutAsync($"/api/alexandria/books/{book.BookUuid}/cover", formContent);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutCover_UnsupportedMime_Returns400()
    {
        var book = await SeedShellBook("Target");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        using var formContent = BuildCoverFormContent([0, 1, 2, 3], contentType: "image/tiff");

        var response = await authed.PutAsync($"/api/alexandria/books/{book.BookUuid}/cover", formContent);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutCover_Over10MB_Returns400()
    {
        var book = await SeedShellBook("Target");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var large = new byte[10 * 1024 * 1024 + 1];
        using var formContent = BuildCoverFormContent(large, contentType: "image/jpeg");

        var response = await authed.PutAsync($"/api/alexandria/books/{book.BookUuid}/cover", formContent);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PutCover_UnknownBook_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        using var formContent = BuildCoverFormContent([0xFF, 0xD8, 0xFF, 0xE0], contentType: "image/jpeg");

        var response = await authed.PutAsync($"/api/alexandria/books/{Guid.NewGuid()}/cover", formContent);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/alexandria/books/{uuid}/cover ----------

    [TestMethod]
    public async Task DeleteCover_UnknownBook_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.DeleteAsync($"/api/alexandria/books/{Guid.NewGuid()}/cover");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<Book> SeedShellBook(string title, MediaType mediaType = MediaType.Ebook, string? coverFilePath = null)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Default Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Default Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = mediaType,
                SourcePath = $"/tmp/{uuid}.epub",
                CoverFilePath = coverFilePath
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Default Author"]);
        return book;
    }

    private static MultipartFormDataContent BuildCoverFormContent(byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "cover", "cover.bin");
        return form;
    }

    private sealed class BookDto
    {
        public Guid BookUuid { get; set; }
        public string Title { get; set; } = "";
        public bool IsArchived { get; set; }
        public ReadingProgressDto? ReadingProgress { get; set; }
    }

    private sealed class ReadingProgressDto
    {
        public string Status { get; set; } = "";
    }
}
