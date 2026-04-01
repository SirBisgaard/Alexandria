using System.Diagnostics;
using System.IO.Compression;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Middleware;

/// <summary>
/// This middleware logs everything so we can later reverse engineer the kobo.  
/// </summary>
/// <param name="next"></param>
/// <param name="logger"></param>
public class KoboProxyLoggingMiddleware(RequestDelegate next, IKoboTrafficLogRepository koboTrafficLogRepository, ILogger<KoboProxyLoggingMiddleware> logger)
{
    private static readonly string[] ExcludedPaths = ["/alexandria/api", "/favicon.ico"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (ExcludedPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
        {
            await next(context);
            return;
        }

        // ── Request ──
        context.Request.EnableBuffering();

        var method = context.Request.Method;
        var url = context.Request.GetEncodedUrl();
        var requestHeaders = string.Join($"{Environment.NewLine}",
            context.Request.Headers.Select(h => $"{h.Key}={h.Value}"));
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        logger.LogInformation("Request: {Method}:{Url}", method, url);

        // ── Response ──
        // Swap the response body with a buffer we can read back
        var originalBodyStream = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        // Read what was written
        responseBuffer.Position = 0;
        
        var contentEncoding = context.Response.Headers.ContainsKey("Content-Encoding") ? 
            context.Response.Headers["Content-Encoding"].ToString() :
            string.Empty;
        
        var responseBody = contentEncoding.Contains("gzip")
            ? await DecompressGzipAsync(responseBuffer)
            : await new StreamReader(responseBuffer).ReadToEndAsync();
        
        var responseHeaders = string.Join($",{Environment.NewLine}",
            context.Response.Headers.Select(h => $"{h.Key}={h.Value}"));

        logger.LogInformation("Response: {Method}:{Url} {StatusCode}", method, url, context.Response.StatusCode);
        
        await koboTrafficLogRepository.Log(new KoboTrafficLog
        {
            Timestamp = DateTime.UtcNow,
            Method = method,
            Url = context.Request.GetDisplayUrl(),
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
            StatusCode = context.Response.StatusCode,
            ResponseHeaders = responseHeaders,
            ResponseBody = responseBody,
            DurationMs = stopwatch.ElapsedMilliseconds
        });


        // Copy the buffered response back to the original stream
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }
    
    private static async Task<string> DecompressGzipAsync(MemoryStream compressed)
    {
        compressed.Position = 0;
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync();
    }
}