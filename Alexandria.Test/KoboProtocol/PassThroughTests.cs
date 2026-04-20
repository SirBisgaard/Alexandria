using System.Net;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Endpoints that Alexandria doesn't intercept (user profile, wishlist, deals, assets, auth
/// refresh, etc.) fall through to <c>KoboController</c>'s catch-all <c>{**path}</c> and are
/// proxied to Kobo transparently. These tests prove the forwarding mechanism works — status
/// code and headers survive the round-trip — without comparing response bodies (some
/// pass-through endpoints carry PII that we deliberately drop from fixtures).
/// </summary>
[TestClass]
public sealed class PassThroughTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task PassThrough_UserWishlist_ForwardsAndReturnsUpstreamStatus()
    {
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Get, "/v1/user/wishlist", HttpStatusCode.OK, """{"Items":[],"TotalCount":0}""");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{RecordedDeviceUuid}/v1/user/wishlist?PageSize=100&PageIndex=0");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, Upstream.Requests.Count);
    }

    [TestMethod]
    public async Task PassThrough_UserProfile_ForwardsAndReturnsUpstreamStatus()
    {
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Get, "/v1/user/profile", HttpStatusCode.OK, """{"UserKey":"test","DisplayName":"Test"}""");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{RecordedDeviceUuid}/v1/user/profile");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PassThrough_Deals_ForwardsAndReturnsUpstreamStatus()
    {
        await SeedRecordedDevice();
        Upstream.RegisterJson(HttpMethod.Get, "/v1/deals", HttpStatusCode.OK, "[]");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{RecordedDeviceUuid}/v1/deals");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PassThrough_UpstreamNotModified_StatusSurvives()
    {
        // Upstream returns 304 Not Modified — Alexandria must forward it intact so the device's
        // ETag-style caching keeps working.
        await SeedRecordedDevice();
        Upstream.Register(HttpMethod.Get, "/v1/assets", _ => new HttpResponseMessage(HttpStatusCode.NotModified));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{RecordedDeviceUuid}/v1/assets?DiffRequests=test");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotModified, response.StatusCode);
    }

    [TestMethod]
    public async Task PassThrough_AnonymousUnregisteredDevice_Returns401()
    {
        // A request to a pass-through endpoint with an unregistered device must fail auth,
        // not leak through to Kobo.
        var unregisteredDevice = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/kobo/{unregisteredDevice}/v1/user/profile");
        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, Upstream.Requests.Count, "auth failure must short-circuit before the proxy");
    }
}
