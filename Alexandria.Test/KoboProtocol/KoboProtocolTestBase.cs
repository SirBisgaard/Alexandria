using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Services;
using Alexandria.Business.Library.Models;
using Alexandria.Tests.Integration;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Base for Kobo-protocol tests. Extends <see cref="IntegrationTestBase"/> with two additions
/// tests need: (1) the same pre-registered Kobo device UUID seen in the recorded traffic, so
/// fixture URLs replay 1:1 without path rewriting, and (2) a <see cref="FakeKoboUpstreamHandler"/>
/// swapped in as the primary handler for the typed <c>KoboStoreProxyService</c> HttpClient so
/// nothing hits the real <c>storeapi.kobo.com</c>.
/// </summary>
public abstract class KoboProtocolTestBase : IntegrationTestBase
{
    /// <summary>
    /// Synthetic device UUID used throughout the fixtures. The extractor anonymizes the real
    /// production device UUID by replacing it with this fixed value everywhere it appears (paths,
    /// URLs inside response bodies, header placeholders). Tests seed a device row with this UUID
    /// so replayed fixture paths route to it directly.
    /// </summary>
    public static readonly Guid RecordedDeviceUuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Synthetic serial we bind to the seeded device so first-contact binding doesn't fire.</summary>
    public const string DeviceSerial = "TEST-SERIAL";

    /// <summary>Canned user-agent used when replaying recorded requests (model code <c>0390</c> → Kobo Libra Colour).</summary>
    public const string DeviceUserAgent = "Mozilla/5.0 (Linux; U; Android 2.0; en-us;) AppleWebKit/538.1 (KHTML, like Gecko) Version/4.0 Mobile Safari/538.1 (Kobo Touch 0390/4.45.23684)";

    protected FakeKoboUpstreamHandler Upstream { get; private set; } = null!;

    protected override void ExtendTestServices(IServiceCollection services)
    {
        Upstream = new FakeKoboUpstreamHandler();

        // Replacing the IKoboProxyService registration with a hand-built instance sidesteps
        // HttpClientFactory entirely. The factory-configured primary handler from Program.cs is
        // hard to override reliably (test-time ConfigureTestServices runs at DI build time but
        // the handler chain is materialized lazily, and layered ConfigurePrimaryHttpMessageHandler
        // calls don't behave the way you'd expect in practice). A direct registration is simpler
        // and guarantees our FakeKoboUpstreamHandler is the only thing the service ever calls.
        services.RemoveAll<IKoboProxyService>();
        services.AddTransient<IKoboProxyService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<KoboStoreProxyService>>();
            var client = new HttpClient(Upstream, disposeHandler: false);
            return new KoboStoreProxyService(client, logger);
        });
    }

    [TestInitialize]
    public void KoboProtocolTestBase_Setup()
    {
        // The default client from IntegrationTestBase attaches a CookieContainerHandler which
        // parses Set-Cookie headers forwarded from upstream responses. Kobo's real responses
        // include cookies with Domain=kobo.com that CookieContainerHandler then rejects when
        // the request URI is localhost. Kobo protocol tests don't care about cookies, so swap
        // the client for one with cookie handling disabled.
        Client.Dispose();
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    /// <summary>
    /// Inserts a <see cref="KoboDevice"/> matching <see cref="RecordedDeviceUuid"/> and binds the
    /// synthetic serial. Call from each test that needs authenticated requests to pass the
    /// Kobo-device auth handler.
    /// </summary>
    protected async Task SeedRecordedDevice()
    {
        var repo = GetService<IKoboDeviceRepository>();
        await repo.Create(new KoboDevice
        {
            DeviceUuid = RecordedDeviceUuid,
            Nickname = "Test Device"
        });
        await repo.BindDevice(RecordedDeviceUuid, DeviceSerial, modelName: "Kobo Libra Colour", modelCode: "0390", firmware: "4.45.23684");
    }

    /// <summary>
    /// Replays a recorded request through the test HttpClient, attaching the canned device-auth
    /// headers so the <c>KoboDevice</c> auth handler sees what the real device would send.
    /// </summary>
    protected async Task<HttpResponseMessage> SendRecordedAsync(RecordedRequest recorded)
    {
        using var request = new HttpRequestMessage(new HttpMethod(recorded.Method), recorded.Path + recorded.Query);

        request.Headers.TryAddWithoutValidation("x-kobo-deviceid", DeviceSerial);
        request.Headers.TryAddWithoutValidation("User-Agent", DeviceUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(recorded.Body))
            request.Content = new StringContent(recorded.Body, Encoding.UTF8, "application/json");

        return await Client.SendAsync(request);
    }

    /// <summary>Builds a URL rooted at the same device path the recorded traffic uses.</summary>
    protected static string DeviceUrl(string relativePath)
    {
        if (!relativePath.StartsWith('/'))
            relativePath = "/" + relativePath;
        return $"/api/kobo/{RecordedDeviceUuid}{relativePath}";
    }

    /// <summary>
    /// Convenience for asserting a response's status code with a message that includes the fixture
    /// name and the response body preview — shape tests fail often while fixtures are first
    /// generated, and the error output needs to be actionable.
    /// </summary>
    protected static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string fixtureName)
    {
        if (response.StatusCode == expected)
            return;
        var preview = await response.Content.ReadAsStringAsync();
        if (preview.Length > 500)
            preview = preview[..500] + "...";
        Assert.Fail($"Fixture '{fixtureName}': expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {preview}");
    }
}
