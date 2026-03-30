using Microsoft.AspNetCore.Http.Extensions;

namespace Alexandria.Api.Middleware;

/// <summary>
/// This middleware logs everything so we can later reverse engineer the kobo.  
/// </summary>
/// <param name="next"></param>
/// <param name="logger"></param>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
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

        await next(context);

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

        // Copy the buffered response back to the original stream
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }
}