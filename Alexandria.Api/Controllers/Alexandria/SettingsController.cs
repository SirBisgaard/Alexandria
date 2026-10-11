using Alexandria.Api.Models;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/settings")]
public class SettingsController(ISettingsService settingsService, ILogger<SettingsController> logger) : AlexandriaControllerBase
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
        var current = await settingsService.Get();
        await settingsService.UpdateSettings(ApiMapper.ToAppSettings(request, current.ComicRendering));
        return NoContent();
    }

    [HttpPost("preview-template")]
    public IActionResult PreviewTemplate([FromBody] PreviewTemplateRequest request)
    {
        var sample = new Book
        {
            Title = "The Great Gatsby",
            Authors = [new Author { Name = "F. Scott Fitzgerald" }],
            Series = new Series { Name = "Modern Classics", Index = 1 },
            Contributors =
            [
                new Contributor { Name = "Jake Gyllenhaal", Role = "Narrator" },
                new Contributor { Name = "Tobey Maguire", Role = "Narrator" }
            ]
        };

        try
        {
            var rendered = FileNameTemplate.Render(sample, request.Template);
            return Ok(new { result = rendered });
        }
        catch (TemplateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
