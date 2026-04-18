using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class MetadataApiTests : IntegrationTestBase
{
    // ---------- GET /api/alexandria/publishers ----------

    [TestMethod]
    public async Task GetPublishers_Empty_ReturnsEmptyArray()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/publishers");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.IsEmpty(body);
    }

    [TestMethod]
    public async Task GetPublishers_WithSeededPublishers_ReturnsDistinct()
    {
        await SeedBook("Book A", publisher: "Penguin");
        await SeedBook("Book B", publisher: "Tor");
        await SeedBook("Book C", publisher: "Penguin");

        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/publishers");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(2, body);
        Assert.Contains("Penguin", body);
        Assert.Contains("Tor", body);
    }

    [TestMethod]
    public async Task GetPublishers_WithoutAuth_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/publishers");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- GET /api/alexandria/contributor-roles ----------

    [TestMethod]
    public async Task GetContributorRoles_AudiobookQuery_ReturnsNarratorOnly()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/contributor-roles?mediaType=Audiobook");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(1, body);
        Assert.AreEqual("Narrator", body[0]);
    }

    [TestMethod]
    public async Task GetContributorRoles_AudiobookQuery_CaseInsensitive()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/contributor-roles?mediaType=AUDIOBOOK");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.HasCount(1, body);
    }

    [TestMethod]
    public async Task GetContributorRoles_EbookQuery_ReturnsFullEpubRoleList()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/contributor-roles?mediaType=Ebook");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.IsGreaterThan(5, body.Length);
        Assert.Contains("Narrator", body);
        Assert.Contains("Author", body);
        Assert.Contains("Editor", body);
        Assert.Contains("Translator", body);
    }

    [TestMethod]
    public async Task GetContributorRoles_NoMediaTypeQuery_DefaultsToEpubRoleList()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/contributor-roles");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.IsNotNull(body);
        Assert.IsGreaterThan(5, body.Length);
        Assert.Contains("Author", body);
    }

    [TestMethod]
    public async Task GetContributorRoles_WithoutAuth_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/contributor-roles");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task SeedBook(string title, string publisher)
    {
        var bookRepo = GetService<IBookRepository>();
        var uuid = Guid.NewGuid();
        await bookRepo.Add(new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            Publisher = publisher,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Test Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Ebook,
                SourcePath = $"/tmp/{uuid}.epub"
            }
        });
    }
}
