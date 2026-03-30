using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;

namespace Alexandria.Business.Services;

public class KoboProxyService(KoboRetailSettings settings, HttpClient httpClient, IKoboProxyLogger koboProxyLogger) : IKoboProxyService
{   
    // Headers that shouldn't be forwarded in either direction
    private static readonly HashSet<string> HopByHopHeaders =
    [
        "connection", "keep-alive", "transfer-encoding",
        "te", "trailer", "upgrade", "host"
    ];
    
    public async Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
    {
        var targetUrl = $"{settings.StoreApi}{request.Path}{request.QueryString}";
        var upstream = new HttpRequestMessage(new HttpMethod(request.Method), targetUrl);

        // Copy body
        if (request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(request.Body);

        // Copy headers
        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key.ToLowerInvariant()))
                continue;

            // Content headers go on the content, not the request
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                && upstream.Content is not null)
            {
                upstream.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            else
            {
                upstream.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Send to Kobo
        var response = await httpClient.SendAsync(upstream);

        // Read the full response
        var responseBody = await response.Content.ReadAsByteArrayAsync();

        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => h.Value.ToArray());

        return new KoboProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = responseHeaders,
            Body = responseBody
        };
    }
}