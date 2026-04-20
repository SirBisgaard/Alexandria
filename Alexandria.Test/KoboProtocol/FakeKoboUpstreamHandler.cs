using System.Net;
using System.Text;

namespace Alexandria.Tests.KoboProtocol;

/// <summary>
/// Stand-in for <c>storeapi.kobo.com</c>. Registered as the primary handler for the typed
/// <c>KoboStoreProxyService</c> HttpClient in <see cref="KoboProtocolTestBase"/>. Tests register
/// canned responses keyed on (method, path prefix); anything unregistered throws so tests can't
/// accidentally depend on network access.
/// </summary>
public sealed class FakeKoboUpstreamHandler : HttpMessageHandler
{
    private readonly List<Registration> registrations = [];

    /// <summary>Requests observed by the handler, in arrival order. Useful for assertions on what Alexandria forwarded.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public void Register(HttpMethod method, string pathPrefix, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        registrations.Add(new Registration(method, pathPrefix, responder));
    }

    /// <summary>Registers a canned JSON response for any request whose method matches and whose path starts with <paramref name="pathPrefix"/>.</summary>
    public void RegisterJson(HttpMethod method, string pathPrefix, HttpStatusCode statusCode, string jsonBody, IDictionary<string, string>? extraHeaders = null)
    {
        Register(method, pathPrefix, _ => BuildJsonResponse(statusCode, jsonBody, extraHeaders));
    }

    /// <summary>Convenience for registering an empty 200 OK JSON array response.</summary>
    public void RegisterEmptyJsonArray(HttpMethod method, string pathPrefix)
    {
        RegisterJson(method, pathPrefix, HttpStatusCode.OK, "[]");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var path = request.RequestUri!.PathAndQuery;
        foreach (var reg in registrations)
        {
            if (reg.Method != request.Method)
                continue;
            if (!path.StartsWith(reg.PathPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            return Task.FromResult(reg.Responder(request));
        }
        throw new InvalidOperationException($"FakeKoboUpstreamHandler: no registration for {request.Method} {path}. Register one in the test's setup or use RegisterEmptyJsonArray as a catch-all.");
    }

    private static HttpResponseMessage BuildJsonResponse(HttpStatusCode statusCode, string jsonBody, IDictionary<string, string>? extraHeaders)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        if (extraHeaders is not null)
        {
            foreach (var (key, value) in extraHeaders)
            {
                if (!response.Headers.TryAddWithoutValidation(key, value))
                    response.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
        return response;
    }

    private sealed record Registration(HttpMethod Method, string PathPrefix, Func<HttpRequestMessage, HttpResponseMessage> Responder)
    {
    }
}
