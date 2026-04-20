using System.Net;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/products/books/series/{uuid} returns the recommendation-style list of books in a series.
/// Local series are served from Alexandria; unknown series UUIDs are proxied to Kobo. The
/// response wraps the items in a <c>KoboStoreProductRecommendations</c> envelope with
/// paging/count fields.
/// </summary>
[TestClass]
public sealed class ProductSeriesTests : KoboProtocolTestBase
{
    private static readonly string[] StrictPaths =
    [
        "Items[].Audiobook.IsRecommendation",
        "Items[].Audiobook.IsFree",
        "Items[].Audiobook.HasPreview"
    ];

    [TestMethod]
    public async Task Series_LocalSeriesWithAudiobook_ShapeMatchesRecordedResponse()
    {
        var fixture = FixtureLoader.LoadAll("products-series").First();
        await SeedRecordedDevice();
        var seriesUuid = await SeedLocalSeriesWithAudiobook();
        var path = RewriteSeriesUuidIn(fixture.Request.Path, seriesUuid);

        using var response = await SendGet(path, fixture.Request.Query);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        JsonShape.AssertMatches(fixture.ExpectedBody!.Value, body, StrictPaths);
    }

    [TestMethod]
    public async Task Series_LocalSeries_WrapperCountsReflectSeededBooks()
    {
        await SeedRecordedDevice();
        var seriesUuid = await SeedLocalSeriesWithAudiobook();

        using var response = await SendGet($"/api/kobo/{RecordedDeviceUuid}/v1/products/books/series/{seriesUuid}", "");

        var body = await ReadJsonAsync(response);
        Assert.AreEqual(1, body.GetProperty("ItemCount").GetInt32(), "one seeded book → ItemCount = 1");
        Assert.AreEqual(1, body.GetProperty("TotalItemCount").GetInt32());
        Assert.AreEqual(1, body.GetProperty("TotalPageCount").GetInt32());
    }

    [TestMethod]
    public async Task Series_UnknownSeries_ProxiesToKoboUpstream()
    {
        var unknownSeriesUuid = Guid.NewGuid();
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Get, $"/v1/products/books/series/{unknownSeriesUuid}", HttpStatusCode.OK,
            """{"Items":[],"ItemCount":0,"TotalItemCount":0,"TotalPageCount":0,"CurrentPageIndex":0,"ItemsPerPage":100,"VersionCode":2}""");

        using var response = await SendGet($"/api/kobo/{RecordedDeviceUuid}/v1/products/books/series/{unknownSeriesUuid}", "");

        await AssertStatusAsync(response, HttpStatusCode.OK, "unknown-series-proxy");
        Assert.AreEqual(1, Upstream.Requests.Count, "unknown series must forward to Kobo");
    }

    private async Task<Guid> SeedLocalSeriesWithAudiobook()
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var seriesRepo = GetService<ISeriesRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var seriesUuid = await seriesRepo.GetOrCreate("Test Series");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = "Test Audiobook",
            Language = "en",
            Description = "Test description",
            Publisher = "Test Publisher",
            PublicationDate = "2026",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = authorUuid, Name = "Test Author" }],
            Series = new Series { SeriesUuid = seriesUuid, Name = "Test Series", Index = 1m },
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
        await seriesRepo.SetBookSeries(uuid, seriesUuid, 1m);
        return seriesUuid;
    }

    private static string RewriteSeriesUuidIn(string recordedPath, Guid localSeriesUuid)
    {
        var marker = "/v1/products/books/series/";
        var idx = recordedPath.IndexOf(marker, StringComparison.Ordinal);
        return recordedPath[..(idx + marker.Length)] + localSeriesUuid;
    }

    private async Task<HttpResponseMessage> SendGet(string path, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path + query);
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        return await Client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
