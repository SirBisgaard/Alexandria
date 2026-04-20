using System.Net;
using System.Text.RegularExpressions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// DELETE /v1/library/{uuid} archives local books (soft delete — <c>IsArchived = true</c>) and
/// proxies unknown UUIDs to Kobo. Returns 204 No Content on success. Requests with a device key
/// that isn't registered in Alexandria fail at the auth handler with 401 — covering the 36
/// recorded 401s from a transient stale device id in the production logs.
/// </summary>
[TestClass]
public sealed class DeleteBookTests : KoboProtocolTestBase
{
    private static readonly Regex EntitlementUuidRegex = new(@"/v1/library/([0-9a-f-]{36})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [TestMethod]
    public async Task Delete_LocalBook_Returns204AndArchivesBook()
    {
        var fixture = FixtureLoader.LoadAll("library-delete-204").Single();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        var book = await SeedLocalBook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.NoContent, fixture.Name);

        var bookService = GetService<IBookService>();
        var archived = await bookService.GetBookByUuid(book.BookUuid);
        Assert.IsNotNull(archived);
        Assert.IsTrue(archived.IsArchived, "local deletion must archive, not hard-delete");
    }

    [TestMethod]
    public async Task Delete_UnknownBook_ProxiesToKoboUpstream()
    {
        var fixture = FixtureLoader.LoadAll("library-delete-204").Single();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        // No local book seeded — Alexandria should forward the DELETE to Kobo.
        Upstream.Register(HttpMethod.Delete, $"/v1/library/{entitlementUuid}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.NoContent, fixture.Name);
        Assert.AreEqual(1, Upstream.Requests.Count);
    }

    [TestMethod]
    public async Task Delete_UnregisteredDevice_Returns401()
    {
        var fixture = FixtureLoader.LoadAll("library-delete-401").Single();
        // Fixture path uses a different device UUID than RecordedDeviceUuid. We deliberately do
        // NOT seed that device, so KoboDeviceAuthHandler fails at "Device not registered" and
        // the request never reaches the proxy.

        using var response = await SendRecordedAsync(fixture.Request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, Upstream.Requests.Count, "unauthorized requests must not reach the upstream Kobo API");
    }

    private async Task<Book> SeedLocalBook(Guid entitlementUuid)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = entitlementUuid,
            Title = "Test Book",
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Test Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Ebook,
                SourcePath = $"/tmp/{uuid}.epub"
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Test Author"]);
        return book;
    }

    private static Guid ExtractEntitlementUuid(string path)
    {
        var match = EntitlementUuidRegex.Match(path);
        if (!match.Success)
            throw new InvalidOperationException($"Couldn't find entitlement UUID in path '{path}'");
        return Guid.Parse(match.Groups[1].Value);
    }
}
