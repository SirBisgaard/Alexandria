using System.Net;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Per-product endpoints: <c>/v1/products/books/{uuid}</c> for ebooks and
/// <c>/v1/products/audiobooks/{uuid}</c> for audiobooks. The recorded production traffic doesn't
/// include a happy-path GET for either (the prod library is entirely user-imported — the device
/// never hits the single-product endpoint for those), so these tests are synthetic: seed a
/// local book, hit the endpoint, and assert the envelope shape that
/// <see cref="Alexandria.Business.KoboStoreProxy.KoboStoreEntityMapper"/> produces.
/// </summary>
[TestClass]
public sealed class ProductTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task EbookProduct_LocalBook_ReturnsBookEnvelope()
    {
        await SeedRecordedDevice();
        var entitlementUuid = Guid.NewGuid();
        await SeedLocalEbook(entitlementUuid);

        var path = $"/api/kobo/{RecordedDeviceUuid}/v1/products/books/{entitlementUuid}";
        using var response = await Get(path);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.AreEqual(entitlementUuid.ToString(), body.GetProperty("Id").GetString());
        Assert.AreEqual("Test Ebook", body.GetProperty("Title").GetString());
        Assert.AreEqual(entitlementUuid.ToString(), body.GetProperty("ImageId").GetString());
    }

    [TestMethod]
    public async Task AudiobookProduct_LocalBook_ReturnsAudiobookEnvelope()
    {
        await SeedRecordedDevice();
        var entitlementUuid = Guid.NewGuid();
        await SeedLocalAudiobook(entitlementUuid);

        var path = $"/api/kobo/{RecordedDeviceUuid}/v1/products/audiobooks/{entitlementUuid}";
        using var response = await Get(path);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.AreEqual(entitlementUuid.ToString(), body.GetProperty("Id").GetString());
        Assert.AreEqual("Test Audiobook", body.GetProperty("Title").GetString());
        // Audiobook product includes Duration; ebook product doesn't.
        Assert.AreEqual(JsonValueKind.Number, body.GetProperty("Duration").ValueKind);
    }

    [TestMethod]
    public async Task EbookProduct_UnknownBook_ProxiesToKoboUpstream()
    {
        await SeedRecordedDevice();
        var unknownUuid = Guid.NewGuid();
        Upstream.RegisterJson(HttpMethod.Get, $"/v1/products/books/{unknownUuid}", HttpStatusCode.OK, """{"Id":"stub"}""");

        using var response = await Get($"/api/kobo/{RecordedDeviceUuid}/v1/products/books/{unknownUuid}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, Upstream.Requests.Count);
    }

    private async Task<HttpResponseMessage> Get(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        return await Client.SendAsync(request);
    }

    private async Task SeedLocalEbook(Guid entitlementUuid)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = entitlementUuid,
            Title = "Test Ebook",
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
    }

    private async Task SeedLocalAudiobook(Guid entitlementUuid)
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = entitlementUuid,
            Title = "Test Audiobook",
            Language = "en",
            Description = "Test description",
            Publisher = "Test Publisher",
            PublicationDate = "2026",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Test Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = MediaType.Audiobook,
                SourcePath = $"/tmp/{uuid}.m4b",
                Duration = 3600,
                AudioBitrate = 64000,
                TotalFileSize = 1_000_000
            }
        };
        await bookRepo.Add(book);
        await authorRepo.SetBookAuthors(uuid, ["Test Author"]);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
