using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Alexandria.Tests.Integration;

[TestClass]
public sealed class ResponseCompressionTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Get_WithAcceptEncodingGzip_ReturnsGzippedJson()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        authed.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");

        var response = await authed.GetAsync("/api/alexandria/books");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);

        await using var compressed = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(gzip);
        Assert.AreEqual(JsonValueKind.Array, body.ValueKind);
    }

    [TestMethod]
    public async Task Get_WithoutAcceptEncoding_ReturnsUncompressedJson()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/books");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsEmpty(response.Content.Headers.ContentEncoding);
    }
}
