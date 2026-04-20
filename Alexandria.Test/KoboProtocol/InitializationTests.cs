using System.Net;
using System.Text.Json;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Tests.KoboProtocol.Shape;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// /v1/initialization is the first request a freshly-booted Kobo makes. Alexandria intercepts
/// Kobo's response and rewrites four resource URLs (library_sync, image_host, image_url_template,
/// image_url_quality_template) so subsequent sync + cover traffic routes through Alexandria
/// instead of Kobo's CDN. Other resource keys pass through unchanged. A non-empty
/// <c>x-kobo-apitoken</c> header is also set — Kobo requires it but doesn't validate contents.
/// </summary>
[TestClass]
public sealed class InitializationTests : KoboProtocolTestBase
{
    [TestMethod]
    public async Task Initialization_Shape_MatchesRecordedResponse()
    {
        var fixture = LoadFixture();
        await SeedRecordedDevice();
        RegisterUpstreamWithRecordedFixtureBody(fixture);

        using var response = await SendRecordedAsync(fixture.Request);

        await AssertStatusAsync(response, HttpStatusCode.OK, fixture.Name);

        var actualBody = await ReadJsonAsync(response);
        Assert.IsNotNull(fixture.ExpectedBody, "fixture must include the recorded initialization body as the shape template");
        JsonShape.AssertMatches(fixture.ExpectedBody.Value, actualBody);
    }

    [TestMethod]
    public async Task Initialization_RewritesFourResourceUrlsToAlexandria()
    {
        var fixture = LoadFixture();
        await SeedRecordedDevice();
        RegisterUpstreamWithRecordedFixtureBody(fixture);

        using var response = await SendRecordedAsync(fixture.Request);
        var body = await ReadJsonAsync(response);

        var resources = body.GetProperty("Resources");
        var deviceBase = $"http://localhost/api/kobo/{RecordedDeviceUuid}";
        Assert.AreEqual($"{deviceBase}/v1/library/sync", resources.GetProperty("library_sync").GetString());
        Assert.AreEqual(deviceBase, resources.GetProperty("image_host").GetString());
        Assert.AreEqual($"{deviceBase}/{{ImageId}}/{{Width}}/{{Height}}/false/image.jpg", resources.GetProperty("image_url_template").GetString());
        Assert.AreEqual($"{deviceBase}/{{ImageId}}/{{Width}}/{{Height}}/{{Quality}}/false/image.jpg", resources.GetProperty("image_url_quality_template").GetString());
    }

    [TestMethod]
    public async Task Initialization_OtherResourceUrls_PassThroughUnchanged()
    {
        var fixture = LoadFixture();
        await SeedRecordedDevice();
        RegisterUpstreamWithRecordedFixtureBody(fixture);

        using var response = await SendRecordedAsync(fixture.Request);
        var body = await ReadJsonAsync(response);
        var resources = body.GetProperty("Resources");

        foreach (var untouched in new[] { "productsv2", "user_profile", "user_wishlist" })
        {
            var expected = fixture.ExpectedBody!.Value.GetProperty("Resources").GetProperty(untouched).GetString();
            Assert.AreEqual(expected, resources.GetProperty(untouched).GetString(), $"resource key '{untouched}' must be forwarded unchanged");
        }
    }

    [TestMethod]
    public async Task Initialization_SetsKoboApiTokenHeader_ToE30()
    {
        var fixture = LoadFixture();
        await SeedRecordedDevice();
        RegisterUpstreamWithRecordedFixtureBody(fixture);

        using var response = await SendRecordedAsync(fixture.Request);

        Assert.IsTrue(response.Headers.TryGetValues("x-kobo-apitoken", out var values), "x-kobo-apitoken must be present");
        Assert.AreEqual("e30=", values!.Single());
    }

    [TestMethod]
    public async Task Initialization_UnregisteredDevice_Returns401()
    {
        var fixture = FixtureLoader.LoadAll("initialization-401").Single();

        using var response = await SendRecordedAsync(fixture.Request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RecordedFixture LoadFixture() => FixtureLoader.LoadAll("initialization-200").Single();

    /// <summary>
    /// Registers the upstream stub to serve the fixture's <c>expectedBody</c> as if it were Kobo's
    /// own initialization response. Alexandria then rewrites the four resource URLs and returns
    /// what the test asserts on.
    /// </summary>
    private void RegisterUpstreamWithRecordedFixtureBody(RecordedFixture fixture)
    {
        var body = fixture.ExpectedBody!.Value.GetRawText();
        Upstream.RegisterJson(HttpMethod.Get, "/v1/initialization", HttpStatusCode.OK, body);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
