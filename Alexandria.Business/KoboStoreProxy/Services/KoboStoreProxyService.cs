using System.IO.Compression;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.KoboStoreProxy.Services;

/// <summary>
/// Transparent HTTP proxy between Kobo e-readers and storeapi.kobo.com.
/// Forwards unintercepted requests.
/// </summary>
public class KoboStoreProxyService(HttpClient httpClient, ILogger<KoboStoreProxyService> logger) : IKoboProxyService
{
    private static readonly HashSet<string> HopByHopHeaders =
    [
        "connection", "keep-alive", "transfer-encoding",
        "te", "trailer", "upgrade", "host"
    ];

    /// <summary>
    /// Transparently forwards a request to storeapi.kobo.com and returns the raw response.
    /// Used as the fallback for store-purchased books and as the catch-all for unintercepted endpoints.
    /// </summary>
    public async Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
    {
        var targetUrl = $"{KoboProxySettings.StoreApi}{request.Path}{request.QueryString}";
        logger.LogInformation("Proxy: {Method} {Url}", request.Method, targetUrl);

        var (response, responseBody) = await ForwardRequest(targetUrl, request);

        return new KoboProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            Headers = BuildResponseHeaders(response),
            Body = responseBody
        };
    }

    /// <summary>
    /// Forwards a request to Kobo's store API. Hop-by-hop headers (RFC 2616 §13.5.1) are filtered
    /// including "host" since we change the target. Gzip responses are decompressed here because
    /// downstream services need to read/modify the body (e.g., merging local books into sync results).
    /// Content-Encoding header is stripped from the response so it isn't double-decompressed.
    /// </summary>
    private async Task<(HttpResponseMessage response, byte[] body)> ForwardRequest(string targetUrl, KoboProxyRequest request)
    {
        var upstream = new HttpRequestMessage(new HttpMethod(request.Method), targetUrl);

        if (request.Body.Length > 0)
            upstream.Content = new ByteArrayContent(request.Body);

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key.ToLowerInvariant()))
                continue;

            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && upstream.Content is not null)
                upstream.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            else
                upstream.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var response = await httpClient.SendAsync(upstream);
        var body = await response.Content.ReadAsByteArrayAsync();
        var contentEncoding = response.Content.Headers.ContentEncoding.FirstOrDefault();

        if (string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
        {
            using var compressed = new MemoryStream(body);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            await gzip.CopyToAsync(decompressed);
            body = decompressed.ToArray();
        }

        return (response, body);
    }

    private static Dictionary<string, string[]> BuildResponseHeaders(HttpResponseMessage response)
    {
        return response.Headers
            .Concat(response.Content.Headers)
            .Where(h => !h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToArray());
    }
}
