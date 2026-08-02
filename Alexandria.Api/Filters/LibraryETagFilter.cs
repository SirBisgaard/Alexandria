using Alexandria.Business.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Alexandria.Api.Filters;

/// <summary>
/// Conditional-GET support for library endpoints. Stamps successful GET responses with a weak
/// ETag derived from the library-wide version token plus <c>Cache-Control: private, no-cache</c>
/// (cache, but revalidate every time). When the client's If-None-Match still matches the current
/// token, the request short-circuits to 304 Not Modified before the action loads any data.
/// Responses that manage their own caching (Cache-Control already set, e.g. cover images) are
/// left untouched.
/// </summary>
public class LibraryETagFilter(ILibraryVersionRepository versionRepository) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method))
        {
            await next();
            return;
        }

        var etag = new EntityTagHeaderValue($"\"{await versionRepository.GetVersionToken()}\"", isWeak: true);
        if (request.GetTypedHeaders().IfNoneMatch.Any(clientTag => clientTag.Compare(etag, useStrongComparison: false)))
        {
            ApplyCacheHeaders(context.HttpContext.Response, etag);
            context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
            return;
        }

        await next();

        var response = context.HttpContext.Response;
        if (response.StatusCode == StatusCodes.Status200OK && StringValues.IsNullOrEmpty(response.Headers.CacheControl))
            ApplyCacheHeaders(response, etag);
    }

    private static void ApplyCacheHeaders(HttpResponse response, EntityTagHeaderValue etag)
    {
        response.GetTypedHeaders().ETag = etag;
        response.Headers.CacheControl = "private, no-cache";
    }
}
