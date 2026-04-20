using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/products/{uuids}/prices serves batch prices — all books Alexandria owns are free
/// (price = 0, currency USD). If the batch contains any local book, only the local prices are
/// returned; the device will issue a separate request for the store-owned UUIDs. If no UUID in
/// the batch is local, the entire request is proxied to Kobo unchanged.
/// </summary>
[TestClass]
public sealed class ProductPricesTests : KoboProtocolTestBase
{
    private static readonly Regex PricesUuidRegex = new(@"/v1/products/([0-9a-f-]{36})/prices", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [TestMethod]
    public async Task Prices_LocalBook_ReturnsZeroPriceInUsd()
    {
        var fixture = FixtureLoader.LoadAll("products-prices").First();
        var entitlementUuid = ExtractUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        await SeedLocalEbook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        JsonShape.AssertMatches(fixture.ExpectedBody!.Value, body);

        var item = body.GetProperty("Items")[0];
        Assert.AreEqual(entitlementUuid.ToString(), item.GetProperty("Id").GetString());
        Assert.AreEqual("USD", item.GetProperty("Price").GetProperty("Currency").GetString());
        Assert.AreEqual(0, item.GetProperty("Price").GetProperty("Price").GetDecimal());
    }

    [TestMethod]
    public async Task Prices_UnknownBook_ProxiesToKoboUpstream()
    {
        var unknownUuid = Guid.NewGuid();
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Get, $"/v1/products/{unknownUuid}/prices", HttpStatusCode.OK, """{"Items":[]}""");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{RecordedDeviceUuid}/v1/products/{unknownUuid}/prices");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, Upstream.Requests.Count);
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

    private static Guid ExtractUuid(string path)
    {
        var match = PricesUuidRegex.Match(path);
        if (!match.Success)
            throw new InvalidOperationException($"Couldn't find UUID in path '{path}'");
        return Guid.Parse(match.Groups[1].Value);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
