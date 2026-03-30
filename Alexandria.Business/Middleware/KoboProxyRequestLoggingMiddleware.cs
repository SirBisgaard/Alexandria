using System.Diagnostics;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Middleware;

/// <summary>
/// This middleware logs everything so we can later reverse engineer the kobo.  
/// </summary>
/// <param name="next"></param>
/// <param name="logger"></param>
public class KoboProxyRequestLoggingMiddleware(RequestDelegate next, IKoboTrafficLogRepository koboTrafficLogRepository, ILogger<KoboProxyRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // ── Request ──
        context.Request.EnableBuffering();

        var method = context.Request.Method;
        var url = context.Request.GetDisplayUrl();
        var requestHeaders = string.Join($"{Environment.NewLine}",
            context.Request.Headers.Select(h => $"{h.Key}={h.Value}"));
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        logger.LogInformation("""
                        Request: {Method}:{Url}
                        HEADERS:
                        {Headers}
                        BODY:
                        {Body}
                        """, method, url, requestHeaders, requestBody);

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
        var contentType = context.Response.ContentType ?? "";
        var responseBody = contentType.StartsWith("application/json") || contentType.StartsWith("text/")
            ? await new StreamReader(responseBuffer).ReadToEndAsync()
            : $"[binary data, {responseBuffer.Length} bytes]";

        var responseHeaders = string.Join($",{Environment.NewLine}",
            context.Response.Headers.Select(h => $"{h.Key}={h.Value}"));

        logger.LogInformation("""
                        Response: {Method}:{Url} {StatusCode}
                        HEADERS:
                        {Headers}
                        BODY:
                        {Body}
                        """, method, url, context.Response.StatusCode,
                        responseHeaders, responseBody);
        
        await koboTrafficLogRepository.Log(new KoboTrafficLog
        {
            Timestamp = DateTime.UtcNow,
            Method = method,
            Url = url,
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
}