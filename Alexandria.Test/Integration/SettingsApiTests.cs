using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Tests.Integration;

/// <summary>
/// Settings tests mutate a process-wide cache inside <c>SettingsService</c>. The base
/// class resets it in <c>[TestInitialize]</c>, but concurrent test methods in this class
/// would still race each other's writes, so the class is pinned to sequential execution.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsApiTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Get_Default_Returns200WithSettingsResponse()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/settings");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SettingsResponseDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(60, body.SyncBatchSize);
        Assert.IsFalse(body.ProxyLoggingEnabled);
        Assert.AreEqual(250, body.WordsPerPage);
        Assert.IsFalse(body.SyncLibraryAtBoot);
    }

    [TestMethod]
    public async Task Get_IncludesVersionFromAssembly()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/settings");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SettingsResponseDto>();
        Assert.IsNotNull(body);
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.Version));
    }

    [TestMethod]
    public async Task Put_AllFields_Returns204NoContent()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload
        {
            SyncBatchSize = 100,
            ProxyLoggingEnabled = false,
            WordsPerPage = 300,
            SyncLibraryAtBoot = true,
            M4BFileTreatment = M4BFileTreatment.ConvertAtIndex
        };

        var response = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(0, response.Content.Headers.ContentLength ?? 0);
    }

    [TestMethod]
    public async Task Put_RoundTripsAllFieldsThroughRepository()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload
        {
            SyncBatchSize = 42,
            ProxyLoggingEnabled = false,
            WordsPerPage = 333,
            SyncLibraryAtBoot = true,
            M4BFileTreatment = M4BFileTreatment.TranscodeAtIndex
        };
        var put = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);
        put.EnsureSuccessStatusCode();

        var get = await authed.GetAsync("/api/alexandria/settings");
        var body = await get.Content.ReadFromJsonAsync<SettingsResponseDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(42, body.SyncBatchSize);
        Assert.IsFalse(body.ProxyLoggingEnabled);
        Assert.AreEqual(333, body.WordsPerPage);
        Assert.IsTrue(body.SyncLibraryAtBoot);
        Assert.AreEqual("TranscodeAtIndex", body.M4BFileTreatment);
    }

    [TestMethod]
    public async Task Put_SyncBatchSizeBelowOne_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload { SyncBatchSize = 0, WordsPerPage = 250 };
        var response = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_SyncBatchSizeAboveThousand_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload { SyncBatchSize = 1001, WordsPerPage = 250 };
        var response = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_WordsPerPageBelowOne_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload { SyncBatchSize = 60, WordsPerPage = 0 };
        var response = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_WordsPerPageAboveThousand_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var payload = new UpdatePayload { SyncBatchSize = 60, WordsPerPage = 1001 };
        var response = await authed.PutAsJsonAsync("/api/alexandria/settings", payload);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_WithoutAuth_Returns401()
    {
        var response = await Client.PutAsJsonAsync("/api/alexandria/settings", new UpdatePayload
        {
            SyncBatchSize = 60,
            WordsPerPage = 250
        });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class SettingsResponseDto
    {
        public DateTimeOffset? LastIndexedTimestamp { get; set; }
        public int SyncBatchSize { get; set; }
        public bool ProxyLoggingEnabled { get; set; }
        public int WordsPerPage { get; set; }
        public bool SyncLibraryAtBoot { get; set; }
        public string M4BFileTreatment { get; set; } = "";
        public string? Version { get; set; }
    }

    private sealed class UpdatePayload
    {
        public int SyncBatchSize { get; set; } = 60;
        public bool ProxyLoggingEnabled { get; set; }
        public int WordsPerPage { get; set; } = 250;
        public bool SyncLibraryAtBoot { get; set; }
        public M4BFileTreatment M4BFileTreatment { get; set; } = M4BFileTreatment.Nothing;
    }
}
