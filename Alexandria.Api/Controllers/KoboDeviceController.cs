using Alexandria.Business.Models;
using Alexandria.Business.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("api/kobo")]
public class KoboDeviceController: ControllerBase
{
    private readonly IKoboProxyService _koboProxyService;

    public KoboDeviceController(IKoboProxyService  koboProxyService)
    {
        _koboProxyService = koboProxyService;
    }
    
    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpDelete("{**path}")]
    public async Task CatchAll([FromRoute]string? path = null)
    {
        var request = await KoboProxyRequest.FromHttpRequest(HttpContext.Request);
        var response = await _koboProxyService.Proxy(request);
        await response.WriteToHttpResponse(HttpContext.Response);
    }
}
