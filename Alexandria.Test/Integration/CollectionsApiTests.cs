using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class CollectionsApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/collections ----------

    [TestMethod]
    public async Task List_NoCollections_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/collections");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_WithCollections_IncludesMediaTypeCounts()
    {
        var ebook = await SeedBook("Ebook", MediaType.Ebook);
        var audiobook = await SeedBook("Audiobook", MediaType.Audiobook);
        await SeedCollection("Mixed Shelf", [ebook.BookUuid, audiobook.BookUuid]);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/collections");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(1, body);
        Assert.AreEqual("Mixed Shelf", body[0].Name);
        Assert.AreEqual(1, body[0].EbookCount);
        Assert.AreEqual(1, body[0].AudiobookCount);
    }

    [TestMethod]
    public async Task List_SortByName_DefaultAsc()
    {
        await SeedCollection("Zeta Shelf", []);
        await SeedCollection("Alpha Shelf", []);
        await SeedCollection("Mu Shelf", []);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/collections");

        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto[]>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Alpha Shelf", body[0].Name);
        Assert.AreEqual("Mu Shelf", body[1].Name);
        Assert.AreEqual("Zeta Shelf", body[2].Name);
    }

    [TestMethod]
    public async Task List_SortByNumberOfBooksDesc_OrdersLargestFirst()
    {
        var book1 = await SeedBook("B1", MediaType.Ebook);
        var book2 = await SeedBook("B2", MediaType.Ebook);
        await SeedCollection("Big", [book1.BookUuid, book2.BookUuid]);
        await SeedCollection("Small", [book1.BookUuid]);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/collections?sort=NumberOfBooks&order=Desc");

        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto[]>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Big", body[0].Name);
        Assert.AreEqual("Small", body[1].Name);
    }

    // ---------- GET /api/alexandria/collections/{uuid} ----------

    [TestMethod]
    public async Task Get_Existing_Returns200WithBooksSortedByTitleAsc()
    {
        var bookZ = await SeedBook("Zeta", MediaType.Ebook);
        var bookA = await SeedBook("Alpha", MediaType.Ebook);
        var collection = await SeedCollection("Lib", [bookZ.BookUuid, bookA.BookUuid]);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/collections/{collection.CollectionUuid}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectionDetailDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Lib", body.Name);
        Assert.HasCount(2, body.Books);
        Assert.AreEqual("Alpha", body.Books[0].Title);
        Assert.AreEqual("Zeta", body.Books[1].Title);
    }

    [TestMethod]
    public async Task Get_Existing_WithSortTitleDesc_OrdersZetaFirst()
    {
        var bookZ = await SeedBook("Zeta", MediaType.Ebook);
        var bookA = await SeedBook("Alpha", MediaType.Ebook);
        var collection = await SeedCollection("Lib", [bookZ.BookUuid, bookA.BookUuid]);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/collections/{collection.CollectionUuid}?sort=Title&order=Desc");

        var body = await response.Content.ReadFromJsonAsync<CollectionDetailDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Zeta", body.Books[0].Title);
        Assert.AreEqual("Alpha", body.Books[1].Title);
    }

    [TestMethod]
    public async Task Get_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/collections/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<Book> SeedBook(string title, MediaType mediaType)
    {
        var repo = GetService<IBookRepository>();
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Author" }],
            File = new BookFile { BookUuid = uuid, MediaType = mediaType, SourcePath = $"/tmp/{uuid}.bin" }
        };
        await repo.Add(book);
        return book;
    }

    private async Task<BookCollection> SeedCollection(string name, List<Guid> bookUuids)
    {
        var repo = GetService<ICollectionRepository>();
        var collection = new BookCollection
        {
            CollectionUuid = Guid.NewGuid(),
            Name = name,
            BookUuids = bookUuids,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        await repo.Upsert(collection);
        return collection;
    }

    private sealed class CollectionResponseDto
    {
        public Guid CollectionUuid { get; set; }
        public string Name { get; set; } = "";
        public int EbookCount { get; set; }
        public int AudiobookCount { get; set; }
    }

    private sealed class CollectionDetailDto
    {
        public Guid CollectionUuid { get; set; }
        public string Name { get; set; } = "";
        public List<BookSummaryDto> Books { get; set; } = [];
    }

    private sealed class BookSummaryDto
    {
        public Guid BookUuid { get; set; }
        public string Title { get; set; } = "";
    }
}
