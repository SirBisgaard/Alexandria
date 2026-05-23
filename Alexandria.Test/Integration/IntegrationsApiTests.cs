using System.Net;
using System.Net.Http.Json;
using Alexandria.Business.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alexandria.Tests.Integration;

/// <summary>
/// Covers /api/alexandria/integrations/audiobookshelf:
/// GET, PUT (with its validation branches), and POST .../sync.
/// IAudioBookShelfSyncIntegration is replaced with a fake so verify and sync
/// short-circuit without trying to talk to a real ABS server.
/// </summary>
[TestClass]
public sealed class IntegrationsApiTests : IntegrationTestBase
{
    private FakeAudioBookShelfSyncIntegration Fake { get; } = new();

    protected override void ExtendTestServices(IServiceCollection services)
    {
        services.RemoveAll<IAudioBookShelfSyncIntegration>();
        services.AddSingleton<IAudioBookShelfSyncIntegration>(Fake);
    }

    [TestMethod]
    public async Task Get_NoIntegrationConfigured_ReturnsBlankShape()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/integrations/audiobookshelf");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>();
        Assert.IsNotNull(body);
        Assert.IsFalse(body.Enabled);
        Assert.AreEqual("", body.ServerUrl);
        Assert.AreEqual("", body.ApiToken);
        Assert.IsNull(body.LastSyncedAt);
    }

    [TestMethod]
    public async Task Put_WithValidEnabledPayload_Returns204AndPersists()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        Fake.VerifyResult = true;

        var put = await authed.PutAsJsonAsync("/api/alexandria/integrations/audiobookshelf",
            new { enabled = true, serverUrl = "https://abs.example.com", apiToken = "tok-123" });

        Assert.AreEqual(HttpStatusCode.NoContent, put.StatusCode);

        var get = await authed.GetAsync("/api/alexandria/integrations/audiobookshelf");
        var body = await get.Content.ReadFromJsonAsync<IntegrationDto>();
        Assert.IsNotNull(body);
        Assert.IsTrue(body.Enabled);
        Assert.AreEqual("https://abs.example.com", body.ServerUrl);
        Assert.AreEqual("tok-123", body.ApiToken);
    }

    [TestMethod]
    public async Task Put_EnabledWithMissingFields_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync("/api/alexandria/integrations/audiobookshelf",
            new { enabled = true, serverUrl = "", apiToken = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_EnabledWithRelativeUrl_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PutAsJsonAsync("/api/alexandria/integrations/audiobookshelf",
            new { enabled = true, serverUrl = "not-a-url", apiToken = "tok" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_EnabledButVerifyFails_Returns400()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        Fake.VerifyResult = false;

        var response = await authed.PutAsJsonAsync("/api/alexandria/integrations/audiobookshelf",
            new { enabled = true, serverUrl = "https://abs.example.com", apiToken = "tok" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Put_DisabledIntegration_SkipsVerifyAndPersists()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);
        Fake.VerifyResult = false; // would normally reject — but disable skips verify

        var put = await authed.PutAsJsonAsync("/api/alexandria/integrations/audiobookshelf",
            new { enabled = false, serverUrl = "", apiToken = "" });

        Assert.AreEqual(HttpStatusCode.NoContent, put.StatusCode);
        Assert.AreEqual(0, Fake.VerifyCalls);
    }

    [TestMethod]
    public async Task PostSync_CallsSyncForUser_AndReturns204()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.PostAsync("/api/alexandria/integrations/audiobookshelf/sync", null);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(1, Fake.SyncForUserCalls);
    }

    [TestMethod]
    public async Task PostSync_WithoutAuth_Returns401()
    {
        var response = await Client.PostAsync("/api/alexandria/integrations/audiobookshelf/sync", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Get_WithoutAuth_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/integrations/audiobookshelf");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class IntegrationDto
    {
        public bool Enabled { get; set; }
        public string ServerUrl { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public DateTimeOffset? LastSyncedAt { get; set; }
    }

    private sealed class FakeAudioBookShelfSyncIntegration : IAudioBookShelfSyncIntegration
    {
        public bool VerifyResult { get; set; } = true;
        public int VerifyCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public int SyncForUserCalls { get; private set; }

        public void Reset()
        {
            VerifyResult = true;
            VerifyCalls = 0;
            SyncCalls = 0;
            SyncForUserCalls = 0;
        }

        public Task<bool> VerifyConnection(string serverUrl, string apiToken)
        {
            VerifyCalls++;
            return Task.FromResult(VerifyResult);
        }

        public Task Sync(CancellationToken cancellationToken)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task SyncForUser(Guid userUuid, CancellationToken cancellationToken)
        {
            SyncForUserCalls++;
            return Task.CompletedTask;
        }
    }
}
