using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alexandria.Api.Sync;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alexandria.Tests.Integration;

/// <summary>
/// <para>
/// Sync endpoint coverage. Indexing runs through the shared <see cref="LibraryTaskBackgroundService"/>
/// with an <see cref="ILibraryTaskBroker"/> mediating between the hosted service and any SSE
/// subscribers. Covers: SSE headers on POST /sync, status shape on GET /library-task/status, 401
/// unauthenticated on both routes, and the conversion-failure error-event flow.
/// </para>
/// <para>
/// <b>Limitation:</b> Reading a streamed response mid-progress (before the server-side
/// subscription completes) hangs under <c>TestServer</c>'s in-memory pipe. The error-event test
/// is reliable because the fake <see cref="ThrowingIndexerService"/> throws synchronously — the
/// broker emits one terminal frame and the subscription closes immediately.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SyncApiTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Sync_ReturnsSseStreamWithCorrectHeaders()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alexandria/sync");
        using var response = await authed.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var cacheControl = response.Headers.CacheControl?.ToString() ?? "";
        StringAssert.Contains(cacheControl, "no-cache");
    }

    [TestMethod]
    public async Task SyncStatus_Idle_ReturnsRunningFalse()
    {
        var token = await LoginAsync();
        using var authed = AuthenticatedClient(token);

        var response = await authed.GetAsync("/api/alexandria/library-task/status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.GetProperty("running").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("snapshot").ValueKind);
    }

    [TestMethod]
    public async Task Sync_WithoutAuth_Returns401()
    {
        var response = await Client.PostAsync("/api/alexandria/sync", content: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SyncStatus_WithoutAuth_Returns401()
    {
        var response = await Client.GetAsync("/api/alexandria/library-task/status");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Sync_ConversionFailure_EmitsFailedFrameWithShortErrorMessage()
    {
        using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Transient<IIndexerService, ThrowingIndexerService>())));
        using var client = await AuthenticatedClientFor(factory);

        var response = await client.PostAsync("/api/alexandria/sync", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var frame = ExtractSingleDataFrame(body);
        using var json = JsonDocument.Parse(frame);
        Assert.IsTrue(json.RootElement.GetProperty("done").GetBoolean(), "terminal frame must carry done: true");
        Assert.IsTrue(json.RootElement.GetProperty("failed").GetBoolean(), "failure frame must carry failed: true");
        Assert.AreEqual("Sync failed at converting", json.RootElement.GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task Sync_ConversionFailure_BrokerReturnsToIdle()
    {
        using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Transient<IIndexerService, ThrowingIndexerService>())));
        using var client = await AuthenticatedClientFor(factory);

        var response = await client.PostAsync("/api/alexandria/sync", content: null);
        await response.Content.ReadAsStringAsync(); // drain so EndRun finishes before we check state

        var broker = factory.Services.GetRequiredService<ILibraryTaskBroker>();
        Assert.IsFalse(broker.IsRunning, "broker should be idle after a failed run — EndRun clears the flag");
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Logs in against the given factory and returns an <see cref="HttpClient"/> with the Bearer
    /// token pre-attached. Needed because tokens are only verifiable by the factory that minted
    /// them — each WebApplicationFactory has its own <c>AuthSecretProvider</c> initialized from
    /// its own boot-time UUID seed.
    /// </summary>
    private static async Task<HttpClient> AuthenticatedClientFor(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/alexandria/auth/login", new { username = "alexander", password = "TheGreat" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static string ExtractSingleDataFrame(string sseBody)
    {
        var frames = sseBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(1, frames, $"expected exactly one SSE data frame; got {frames.Length}: {sseBody}");
        var trimmed = frames[0].TrimStart();
        Assert.StartsWith("data: ", trimmed);
        return trimmed["data: ".Length..];
    }

    /// <summary>Fake <see cref="IIndexerService"/> that throws the same abort-signal exception
    /// the real indexer throws when any converter reports errors.</summary>
    internal sealed class ThrowingIndexerService : IIndexerService
    {
        public Task<int> Index() => throw new InvalidOperationException("Sync failed at converting");
        public Task ReindexBook(Guid uuid, Book? desiredMetadata = null, CoverUpdate? coverUpdate = null) => Task.CompletedTask;
    }
}
