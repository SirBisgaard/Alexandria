using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("/")]
public class KoboController(IKoboProxyService koboProxyService) : ControllerBase
{
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