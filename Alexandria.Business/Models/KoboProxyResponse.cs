using Microsoft.AspNetCore.Http;

namespace Alexandria.Business.Models;

public class KoboProxyResponse
{
    public required int StatusCode { get; init; }
    public required Dictionary<string, string[]> Headers { get; init; }
    public required byte[] Body { get; init; }

    public async Task WriteToHttpResponse(HttpResponse response)
    {
        response.StatusCode = StatusCode;

        // Headers that the framework manages — skip these to avoid conflicts
        HashSet<string> skipHeaders = ["transfer-encoding", "content-length", "content-type"];

        foreach (var header in Headers)
        {
            if (skipHeaders.Contains(header.Key.ToLowerInvariant()))
                continue;

            response.Headers[header.Key] = header.Value;
        }

        // Preserve content-type explicitly
        if (Headers.TryGetValue("Content-Type", out var contentType))
            response.ContentType = contentType.First();

        if (Body.Length > 0)
            await response.Body.WriteAsync(Body);
    }
}