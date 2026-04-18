using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class SeriesApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/series ----------

    [TestMethod]
    public async Task List_NoSeries_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/series");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SeriesDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_SeriesWithoutBooks_AreFilteredOut()
    {
        var seriesRepo = GetService<ISeriesRepository>();
        await seriesRepo.GetOrCreate("Lonely Series");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/series");

        var body = await response.Content.ReadFromJsonAsync<SeriesDto[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task List_WithBooks_ReturnsPopulatedSeries()
    {
        await SeedBookInSeries("Book 1", "The Big Series", index: 1);
        await SeedBookInSeries("Book 2", "The Big Series", index: 2);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/series");

        var body = await response.Content.ReadFromJsonAsync<SeriesDto[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(1, body);
        Assert.AreEqual("The Big Series", body[0].Name);
        Assert.HasCount(2, body[0].Books);
    }

    [TestMethod]
    public async Task List_SortByNumberOfBooksDesc_OrdersLargestFirst()
    {
        await SeedBookInSeries("A", "Big", index: 1);
        await SeedBookInSeries("B", "Big", index: 2);
        await SeedBookInSeries("C", "Big", index: 3);
        await SeedBookInSeries("D", "Small", index: 1);

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/series?sort=NumberOfBooks&order=Desc");

        var body = await response.Content.ReadFromJsonAsync<SeriesDto[]>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Big", body[0].Name);
        Assert.AreEqual("Small", body[1].Name);
    }

    // ---------- GET /api/alexandria/series/{uuid} ----------

    [TestMethod]
    public async Task Get_Existing_Returns200WithBooksOrderedByIndexAsc()
    {
        await SeedBookInSeries("Three", "Target", index: 3);
        await SeedBookInSeries("One", "Target", index: 1);
        await SeedBookInSeries("Two", "Target", index: 2);
        var series = await FindSeriesByName("Target");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/series/{series.SeriesUuid}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SeriesDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Target", body.Name);
        Assert.HasCount(3, body.Books);
        Assert.AreEqual("One", body.Books[0].Title);
        Assert.AreEqual("Two", body.Books[1].Title);
        Assert.AreEqual("Three", body.Books[2].Title);
    }

    [TestMethod]
    public async Task Get_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync($"/api/alexandria/series/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/alexandria/series/{uuid} ----------

    [TestMethod]
    public async Task Put_Rename_Returns200WithUpdatedName()
    {
        var seriesRepo = GetService<ISeriesRepository>();
        var uuid = await seriesRepo.GetOrCreate("Old Name");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/series/{uuid}", new { name = "New Name" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SeriesDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("New Name", body.Name);
    }

    [TestMethod]
    public async Task Put_RenameToExistingSeries_MergesAndReturnsTarget()
    {
        await SeedBookInSeries("Target Book", "Target Series", index: 1);
        var seriesRepo = GetService<ISeriesRepository>();
        var sourceUuid = await seriesRepo.GetOrCreate("Source Series");
        var target = (await seriesRepo.FindByName("Target Series"))!;

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/series/{sourceUuid}", new { name = "Target Series" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SeriesDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(target.SeriesUuid, body.SeriesUuid);
        Assert.AreEqual("Target Series", body.Name);
        Assert.HasCount(1, body.Books);
    }

    [TestMethod]
    public async Task Put_WhitespaceOnlyName_Returns400()
    {
        var seriesRepo = GetService<ISeriesRepository>();
        var uuid = await seriesRepo.GetOrCreate("Original");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/series/{uuid}", new { name = "   " });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_EmptyName_Returns400()
    {
        var seriesRepo = GetService<ISeriesRepository>();
        var uuid = await seriesRepo.GetOrCreate("Original");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/series/{uuid}", new { name = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_Unknown_Returns404()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/series/{Guid.NewGuid()}", new { name = "Ghost" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task SeedBookInSeries(string title, string seriesName, decimal index)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var seriesRepo = GetService<ISeriesRepository>();

        var authorUuid = await authorRepo.GetOrCreate("Default Author");
        var seriesUuid = await seriesRepo.GetOrCreate(seriesName);
        var bookUuid = Guid.NewGuid();

        await bookRepo.Add(new Book
        {
            BookUuid = bookUuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Default Author" }],
            Series = new Series { SeriesUuid = seriesUuid, Name = seriesName, Index = index },
            File = new BookFile { BookUuid = bookUuid, MediaType = MediaType.Ebook, SourcePath = $"/tmp/{bookUuid}.epub" }
        });
        await authorRepo.SetBookAuthors(bookUuid, ["Default Author"]);
        await seriesRepo.SetBookSeries(bookUuid, seriesUuid, index);
    }

    private async Task<Series> FindSeriesByName(string name)
    {
        var repo = GetService<ISeriesRepository>();
        return (await repo.FindByName(name))!;
    }

    private sealed class SeriesDto
    {
        public Guid SeriesUuid { get; set; }
        public string Name { get; set; } = "";
        public List<BookSummaryDto> Books { get; set; } = [];
    }

    private sealed class BookSummaryDto
    {
        public Guid BookUuid { get; set; }
        public string Title { get; set; } = "";
    }
}
