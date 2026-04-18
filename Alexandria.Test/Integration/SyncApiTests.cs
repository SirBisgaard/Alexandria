using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.Integration;

/// <summary>
/// <para>
/// Sync endpoint coverage. The controller streams Server-Sent Events on the 200 path and
/// returns a static JSON body on the 409 path. We cover the headers of the SSE response,
/// the 409 lock-contention branch, and the unauthenticated 401 path.
/// </para>
/// <para>
/// <b>Limitation:</b> Reading the full SSE body (progress + completion events) hangs under
/// <c>TestServer</c> because the in-memory pipe that backs the response doesn't surface
/// EOF cleanly for streaming responses. The SSE payload shape and sequencing are covered
/// by Section 2 business tests on <c>IndexerService</c> and the protocol-level tests.
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
}
