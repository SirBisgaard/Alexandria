using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class ETagCachingTests : IntegrationTestBase
{
    [TestMethod]
    public async Task List_Get_ReturnsWeakETagAndPrivateNoCache()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/books");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(response.Headers.ETag);
        Assert.IsTrue(response.Headers.ETag.IsWeak, "Library ETags must be weak so they compose with content encoding.");
        Assert.IsNotNull(response.Headers.CacheControl);
        Assert.IsTrue(response.Headers.CacheControl.Private);
        Assert.IsTrue(response.Headers.CacheControl.NoCache);
    }

    [TestMethod]
    public async Task List_GetWithMatchingIfNoneMatch_Returns304WithoutBody()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var first = await authed.GetAsync("/api/alexandria/books");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/alexandria/books");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await authed.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotModified, second.StatusCode);
        Assert.AreEqual(first.Headers.ETag!.Tag, second.Headers.ETag!.Tag);
        Assert.IsEmpty(await second.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task List_GetWithStaleIfNoneMatch_Returns200WithNewETag()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        var first = await authed.GetAsync("/api/alexandria/books");

        await SeedShellBook("Fresh Arrival");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/alexandria/books");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await authed.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreNotEqual(first.Headers.ETag!.Tag, second.Headers.ETag!.Tag);
    }

    [TestMethod]
    public async Task AuthorsAndSeries_Get_CarryTheSameLibraryETag()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var books = await authed.GetAsync("/api/alexandria/books");
        var authors = await authed.GetAsync("/api/alexandria/authors");
        var series = await authed.GetAsync("/api/alexandria/series");

        Assert.AreEqual(books.Headers.ETag!.Tag, authors.Headers.ETag!.Tag);
        Assert.AreEqual(books.Headers.ETag!.Tag, series.Headers.ETag!.Tag);
    }

    [TestMethod]
    public async Task Mutation_Put_CarriesNoETag()
    {
        var book = await SeedShellBook("Status Target");
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/status", new { status = "Reading" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNull(response.Headers.ETag);
    }

    private async Task<Book> SeedShellBook(string title)
    {
        var bookRepo = GetService<IBookRepository>();
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Ebook,
                SourcePath = $"/tmp/{uuid}.epub"
            }
        };
        await bookRepo.Add(book);
        return book;
    }
}
