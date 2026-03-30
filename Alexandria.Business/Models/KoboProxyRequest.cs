using Microsoft.AspNetCore.Http;

namespace Alexandria.Business.Models;

public class KoboProxyRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string QueryString { get; init; }
    public required Dictionary<string, string?[]> Headers { get; init; }
    public required byte[] Body { get; init; }

    public static async Task<KoboProxyRequest> FromHttpRequest(HttpRequest request)
    {
        request.EnableBuffering();

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        request.Body.Position = 0;

        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray());

        return new KoboProxyRequest
        {
            Method = request.Method,
            Path = request.Path,
            QueryString = request.QueryString.ToString(),
            Headers = headers,
            Body = ms.ToArray()
        };
    }
}