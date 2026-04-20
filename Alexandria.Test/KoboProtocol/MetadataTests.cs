using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/library/{uuid}/metadata returns full book metadata for a single entitlement. Local books
/// are served directly from Alexandria; unknown UUIDs are proxied to Kobo. The response is a JSON
/// array of exactly one metadata object.
/// </summary>
[TestClass]
public sealed class MetadataTests : KoboProtocolTestBase
{
    private static readonly Regex EntitlementUuidRegex = new(@"/v1/library/([0-9a-f-]{36})/metadata", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] StrictPaths =
    [
        "[].DownloadUrls[].Format",
        "[].DownloadUrls[].DrmType",
        "[].DownloadUrls[].Platform",
        "[].Genre",
        "[].IsSocialEnabled"
    ];

    [TestMethod]
    public async Task Metadata_LocalAudiobook_ShapeMatchesRecordedResponse()
    {
        var fixture = FixtureLoader.LoadAll("library-metadata").First();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        await SeedLocalAudiobook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        Assert.AreEqual(JsonValueKind.Array, body.ValueKind, "metadata response must be a JSON array");
        Assert.AreEqual(1, body.GetArrayLength(), "metadata array must contain exactly one item");
        JsonShape.AssertMatches(fixture.ExpectedBody!.Value, body, StrictPaths);
    }

    [TestMethod]
    public async Task Metadata_LocalAudiobook_EntitlementIdMatchesRequestedUuid()
    {
        var fixture = FixtureLoader.LoadAll("library-metadata").First();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        await SeedLocalAudiobook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        var body = await ReadJsonAsync(response);
        var metadata = body[0];
        Assert.AreEqual(entitlementUuid.ToString(), metadata.GetProperty("EntitlementId").GetString());
        Assert.AreEqual(entitlementUuid.ToString(), metadata.GetProperty("CrossRevisionId").GetString());
        Assert.AreEqual(entitlementUuid.ToString(), metadata.GetProperty("RevisionId").GetString());
        Assert.AreEqual(entitlementUuid.ToString(), metadata.GetProperty("WorkId").GetString());
    }

    [TestMethod]
    public async Task Metadata_UnknownBook_ProxiesToKoboUpstream()
    {
        var fixture = FixtureLoader.LoadAll("library-metadata").First();
        await SeedRecordedDevice();
        // No local book seeded — Alexandria should forward the request to Kobo.
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        var proxiedBody = $$"""[{"EntitlementId":"{{entitlementUuid}}","Stub":true}]""";
        Upstream.RegisterJson(HttpMethod.Get, $"/v1/library/{entitlementUuid}/metadata", HttpStatusCode.OK, proxiedBody);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        Assert.AreEqual(1, Upstream.Requests.Count, "unknown UUIDs must round-trip to Kobo");
        var body = await ReadJsonAsync(response);
        Assert.IsTrue(body[0].GetProperty("Stub").GetBoolean(), "proxied response body must pass through unchanged");
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

    private static Guid ExtractEntitlementUuid(string path)
    {
        var match = EntitlementUuidRegex.Match(path);
        if (!match.Success)
            throw new InvalidOperationException($"Couldn't find entitlement UUID in path '{path}'");
        return Guid.Parse(match.Groups[1].Value);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
