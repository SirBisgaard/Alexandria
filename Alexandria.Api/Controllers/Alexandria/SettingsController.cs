using Alexandria.Api.Models;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/settings")]
public class SettingsController(IAlexandriaSettingsService settingsService, ILogger<SettingsController> logger) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await settingsService.Get();
        return Ok(ApiMapper.ToSettingsResponse(settings));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        logger.LogInformation("Updating application settings");
        await settingsService.UpdateSettings(ApiMapper.ToAppSettings(request));
        return NoContent();
    }
}
