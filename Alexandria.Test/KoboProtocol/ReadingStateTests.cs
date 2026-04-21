using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/library/{uuid}/state handles reading progress. GET returns persisted state (or a default
/// "ReadyToRead" state for books the device has never synced). PUT stores updates with a few
/// Kobo-specific invariants: <c>TimesStartedReading</c> only increments on transition <i>into</i>
/// Reading status, the server's <c>PriorityTimestamp</c> overrides whatever the client sent, and
/// stale PUTs (incoming timestamp older than stored) are silently accepted rather than 409'd.
/// </summary>
[TestClass]
public sealed class ReadingStateTests : KoboProtocolTestBase
{
    private static readonly Regex EntitlementUuidRegex = new(@"/v1/library/([0-9a-f-]{36})/state", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [TestMethod]
    public async Task Get_LocalBookWithNoState_ReturnsDefaultReadyToReadShape()
    {
        var fixture = FixtureLoader.LoadAll("library-state-get").Single();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        await SeedLocalEbook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        Assert.AreEqual(JsonValueKind.Array, body.ValueKind);
        Assert.AreEqual(1, body.GetArrayLength());
        JsonShape.AssertMatches(fixture.ExpectedBody!.Value, body, "[].StatusInfo.Status");

        var item = body[0];
        Assert.AreEqual(entitlementUuid.ToString(), item.GetProperty("EntitlementId").GetString());
        Assert.AreEqual("ReadyToRead", item.GetProperty("StatusInfo").GetProperty("Status").GetString());
    }

    [TestMethod]
    public async Task Put_LocalBook_ReturnsUpdateResultShape()
    {
        var fixture = FixtureLoader.LoadAll("library-state-put").Single();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        await SeedLocalEbook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        var body = await ReadJsonAsync(response);
        JsonShape.AssertMatches(fixture.ExpectedBody!.Value, body,
            "RequestResult",
            "UpdateResults[].CurrentBookmarkResult.Result",
            "UpdateResults[].StatisticsResult.Result",
            "UpdateResults[].StatusInfoResult.Result");

        Assert.AreEqual("Success", body.GetProperty("RequestResult").GetString());
        Assert.AreEqual(entitlementUuid.ToString(), body.GetProperty("UpdateResults")[0].GetProperty("EntitlementId").GetString());
    }

    [TestMethod]
    public async Task Put_LocalBook_TransitionToReading_IncrementsTimesStartedReading()
    {
        var fixture = FixtureLoader.LoadAll("library-state-put").Single();
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        await SeedRecordedDevice();
        var book = await SeedLocalEbook(entitlementUuid);

        using var response = await SendRecordedAsync(fixture.Request);
        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);

        var readingStateRepo = GetService<IReadingStateRepository>();
        var stored = await readingStateRepo.GetByBookUuid(book.BookUuid);
        Assert.IsNotNull(stored);
        Assert.AreEqual("Reading", stored.StatusInfo.Status);
        Assert.AreEqual(1, stored.StatusInfo.TimesStartedReading);
    }

    [TestMethod]
    public async Task Put_UnknownBook_ProxiesToKoboUpstream()
    {
        var fixture = FixtureLoader.LoadAll("library-state-put").Single();
        await SeedRecordedDevice();
        // No local book — must round-trip to Kobo.
        var entitlementUuid = ExtractEntitlementUuid(fixture.Request.Path);
        Upstream.RegisterJson(HttpMethod.Put, $"/v1/library/{entitlementUuid}/state", HttpStatusCode.OK,
            """{"RequestResult":"Success","UpdateResults":[]}""");

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);
        Assert.AreEqual(1, Upstream.Requests.Count, "unknown UUIDs must forward to Kobo");
    }

    private async Task<Book> SeedLocalEbook(Guid entitlementUuid)
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
        return book;
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
