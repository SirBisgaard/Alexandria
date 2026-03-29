using Alexandria.Business.Services;
using Microsoft.Extensions.Primitives;

namespace Alexandria.Api.Middleware;

/// <summary>
/// This middleware logs everything so we can later reverse engineer the kobo.  
/// </summary>
/// <param name="next"></param>
/// <param name="logger"></param>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, IKoboProxyLogger koboProxyLogger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Log Path
        logger.LogDebug("Request: {Request}", context.Request.Path);
        
        // Log headers
        var headers = string.Join(", ", 
            context.Request.Headers.Select(h => $"{h.Key}={h.Value}"));
        logger.LogDebug("Headers: {Headers}", headers);

        // Buffer the request body so it can be read and still passed along
        context.Request.EnableBuffering();

        using var reader = new StreamReader(
            context.Request.Body, 
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0; // rewind for downstream handlers

        if (!string.IsNullOrWhiteSpace(body))
            logger.LogDebug("Body: {Body}", body);

        koboProxyLogger.FromKoboDevice(context.Request.Method, context.Request.Path, context.Request.Headers.Select(h => new KeyValuePair<string, StringValues>(h.Key, h.Value)), body);
        
        await next(context);
    }
}