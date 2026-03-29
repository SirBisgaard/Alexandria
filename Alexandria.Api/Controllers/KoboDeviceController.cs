using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("api/kobo")]
public class KoboDeviceController: ControllerBase
{
    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpDelete("{**path}")]
    public IActionResult CatchAll(string path)
    {
        var headers = Request.Headers;
        var route = Request.Path.Value;

        // path will contain everything after api/kobo/
        // "dfsfdsf", "Test", "con/ftet", etc.
        return Ok(new { caught = path });
    }
}
