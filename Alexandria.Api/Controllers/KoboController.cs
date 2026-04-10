using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

/// <summary>
/// Catch-all controller that transparently proxies any request not matched by
/// <see cref="KoboDeviceController"/> to storeapi.kobo.com. This ensures the Kobo device
/// continues to function normally for all endpoints Alexandria does not intercept.
/// </summary>
[ApiController]
[Route("/api/kobo")]
public class KoboController(IKoboProxyService koboProxyService) : ControllerBase
{
    /// <summary>
    /// Forwards unmatched requests to Kobo's store API and returns the raw response.
    /// </summary>
    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpDelete("{**path}")]
    public async Task CatchAll([FromRoute]string? path = null)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await koboProxyService.Proxy(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}
