using System.Net;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/library/sync is the highest-blast-radius endpoint in the proxy — Alexandria's core value
/// is that it merges local books into the Kobo sync response without the device noticing the
/// deception. These tests seed one local audiobook (matching the shape family the recorded
/// production traffic exercises), stub out the upstream Kobo API, and compare Alexandria's
/// response against the recorded baseline both structurally and on the strict fields that break
/// the device when they drift.
/// </summary>
[TestClass]
public sealed class SyncResponseTests : KoboProtocolTestBase
{
    private static readonly string[] StrictPaths =
    [
        // Entitlement invariants — wrong values silently break the device.
        "NewEntitlement.AudiobookEntitlement.OriginCategory",
        "NewEntitlement.AudiobookEntitlement.Status",
        "NewEntitlement.AudiobookEntitlement.IsRemoved",
        "NewEntitlement.AudiobookEntitlement.IsLocked",
        "NewEntitlement.AudiobookEntitlement.Accessibility",
        // Metadata format + Drm shape are load-bearing for the download pipeline.
        "NewEntitlement.AudiobookMetadata.DownloadUrls[].Format",
        "NewEntitlement.AudiobookMetadata.DownloadUrls[].DrmType",
        "NewEntitlement.AudiobookMetadata.DownloadUrls[].Platform",
        "NewEntitlement.AudiobookMetadata.Genre",
        "NewEntitlement.AudiobookMetadata.IsSocialEnabled"
    ];

    [TestMethod]
    public async Task Sync_LocalAudiobook_ShapeMatchesRecordedNewEntitlement()
    {
        var fixture = FixtureLoader.LoadAll("library-sync").First();
        await SeedRecordedDevice();
        await SeedLocalAudiobook();
        // Upstream returns no Kobo-side books so the response contains exactly our seeded item.
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        var template = FindAudiobookTemplate(fixture);
        var actual = FindFirstAudiobookNewEntitlement(body);
        Assert.IsNotNull(actual, "Alexandria should have merged the seeded audiobook into the sync response");
        JsonShape.AssertMatches(template, actual.Value, StrictPaths);
    }

    [TestMethod]
    public async Task Sync_LocalAudiobook_EntitlementIdsAllMatch()
    {
        await SeedRecordedDevice();
        var book = await SeedLocalAudiobook();
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");
        var fixture = FixtureLoader.LoadAll("library-sync").First();

        using var response = await SendRecordedAsync(fixture.Request);
        var body = await ReadJsonAsync(response);
        var item = FindFirstAudiobookNewEntitlement(body);
        Assert.IsNotNull(item);

        var entitlement = item.Value.GetProperty("NewEntitlement").GetProperty("AudiobookEntitlement");
        var entitlementId = book.EntitlementUuid.ToString();
        Assert.AreEqual(entitlementId, entitlement.GetProperty("CrossRevisionId").GetString());
        Assert.AreEqual(entitlementId, entitlement.GetProperty("Id").GetString());
        Assert.AreEqual(entitlementId, entitlement.GetProperty("RevisionId").GetString());
    }

    [TestMethod]
    public async Task Sync_LocalAudiobook_TimestampFormatHasZSuffix_NoOffset()
    {
        await SeedRecordedDevice();
        await SeedLocalAudiobook();
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");
        var fixture = FixtureLoader.LoadAll("library-sync").First();

        using var response = await SendRecordedAsync(fixture.Request);
        var body = await ReadJsonAsync(response);
        var entitlement = FindFirstAudiobookNewEntitlement(body)!.Value.GetProperty("NewEntitlement").GetProperty("AudiobookEntitlement");

        foreach (var field in new[] { "Created", "LastModified" })
        {
            var value = entitlement.GetProperty(field).GetString();
            Assert.IsNotNull(value, $"{field} must not be null");
            Assert.EndsWith("Z", value, $"{field} must use 'yyyy-MM-ddTHH:mm:ssZ' — Kobo rejects '+00:00' offsets");
            Assert.IsFalse(value.Contains('+'), $"{field} must not contain '+' offset");
        }
    }

    [TestMethod]
    public async Task Sync_LocalAudiobook_ManifestDownloadUrl_RoutedThroughAlexandria()
    {
        await SeedRecordedDevice();
        var book = await SeedLocalAudiobook();
        Upstream.RegisterEmptyJsonArray(HttpMethod.Get, "/v1/library/sync");
        var fixture = FixtureLoader.LoadAll("library-sync").First();

        using var response = await SendRecordedAsync(fixture.Request);
        var body = await ReadJsonAsync(response);
        var metadata = FindFirstAudiobookNewEntitlement(body)!.Value.GetProperty("NewEntitlement").GetProperty("AudiobookMetadata");

        var downloadUrl = metadata.GetProperty("DownloadUrls")[0];
        Assert.AreEqual("MANIFEST", downloadUrl.GetProperty("Format").GetString());
        var expectedUrl = $"http://localhost/api/kobo/{RecordedDeviceUuid}/download/{book.EntitlementUuid}/manifest";
        Assert.AreEqual(expectedUrl, downloadUrl.GetProperty("Url").GetString());
    }

    private async Task<Book> SeedLocalAudiobook()
    {
        var bookRepo = GetService<IBookRepository>();
        var authorRepo = GetService<IAuthorRepository>();
        var authorUuid = await authorRepo.GetOrCreate("Test Author");
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = "Test Audiobook",
            Language = "en",
            // Non-null Description / Duration so the audiobook metadata shape lines up with the
            // recorded fixture (both fields get emitted only when non-null, and the fixture has them).
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
        return book;
    }

    private static JsonElement FindAudiobookTemplate(RecordedFixture fixture)
    {
        foreach (var item in fixture.ExpectedBody!.Value.EnumerateArray())
        {
            if (item.TryGetProperty("NewEntitlement", out var e) && e.TryGetProperty("AudiobookEntitlement", out _))
                return item;
        }
        throw new InvalidOperationException("recorded sync fixture has no NewEntitlement audiobook item");
    }

    private static JsonElement? FindFirstAudiobookNewEntitlement(JsonElement response)
    {
        foreach (var item in response.EnumerateArray())
        {
            if (item.TryGetProperty("NewEntitlement", out var e) && e.TryGetProperty("AudiobookEntitlement", out _))
                return item;
        }
        return null;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
