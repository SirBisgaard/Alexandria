using System.Net;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Cover images live at <c>/{device}/{uuid}/{width}/{height}/[{quality}/]{greyscale}/image.jpg</c>.
/// For local books Alexandria serves the JPEG bytes directly. For anything else the response is
/// a 307 (not 302 — 307 preserves the device's HEAD method so cover-probe requests don't
/// downgrade to GET) pointing at Kobo's CDN. These tests focus on the redirect path; covers for
/// local books are exercised by the existing ebook/audiobook metadata tests.
/// </summary>
[TestClass]
public sealed class CoverImageTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task CoverImage_UnknownUuid_Returns307RedirectToKoboCdn()
    {
        await SeedRecordedDevice();
        var unknownUuid = Guid.NewGuid();
        var path = $"/api/kobo/{RecordedDeviceUuid}/{unknownUuid}/355/530/80/false/image.jpg";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.IsNotNull(response.Headers.Location);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://cdn.kobo.com/book-images/", location);
        Assert.IsTrue(location.Contains(unknownUuid.ToString()), "redirect must embed the requested UUID");
    }

    [TestMethod]
    public async Task CoverImage_AllowAnonymous_NoDeviceAuthRequired()
    {
        // The cover endpoint is [AllowAnonymous] — covers are requested by the device without
        // auth headers, so the route must work even with no device registration.
        var unknownUuid = Guid.NewGuid();
        var path = $"/api/kobo/{RecordedDeviceUuid}/{unknownUuid}/355/530/80/false/image.jpg";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }
}
