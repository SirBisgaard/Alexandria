using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alexandria.Tests.Integration;

/// <summary>
/// <para>
/// Sync endpoint coverage. The controller streams Server-Sent Events on the 200 path and
/// returns a static JSON body on the 409 path. Covers: SSE headers, 409 lock-contention,
/// 401 unauthenticated, and the conversion-failure error-event flow introduced by
/// IndexerService's abort-on-conversion-error contract.
/// </para>
/// <para>
/// <b>Limitation:</b> Reading a streamed response mid-progress (before the controller returns)
/// hangs under <c>TestServer</c>'s in-memory pipe. The error-event test is reliable because
/// the fake <see cref="ThrowingIndexerService"/> throws synchronously — the controller writes
/// one frame and returns immediately, letting the client read the full body.
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
    public async Task Sync_AlreadyInProgress_Returns409WithJsonErrorBody()
    {
        var syncLock = GetService<LibrarySyncLock>();
        Assert.IsTrue(syncLock.TryAcquire(), "Pre-acquire of LibrarySyncLock failed — another test left it held");

        try
        {
            var token = await LoginAsync();
            using var authed = AuthenticatedClient(token);

            var response = await authed.PostAsync("/api/alexandria/sync", content: null);

            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual("sync already in progress", body.GetProperty("error").GetString());
        }
        finally
        {
            syncLock.Release();
        }
    }

    [TestMethod]
    public async Task Sync_WithoutAuth_Returns401()
    {
        var response = await Client.PostAsync("/api/alexandria/sync", content: null);

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
    public async Task Sync_ConversionFailure_ReleasesLibrarySyncLock()
    {
        using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Transient<IIndexerService, ThrowingIndexerService>())));
        using var client = await AuthenticatedClientFor(factory);

        var response = await client.PostAsync("/api/alexandria/sync", content: null);
        await response.Content.ReadAsStringAsync(); // drain so the server-side finally runs

        var syncLock = factory.Services.GetRequiredService<LibrarySyncLock>();
        Assert.IsTrue(syncLock.TryAcquire(), "LibrarySyncLock was not released after a failed sync");
        syncLock.Release();
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
