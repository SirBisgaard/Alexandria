using Alexandria.Business.Library.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria")]
public class MetadataController(IBookService bookService) : AlexandriaControllerBase
{
    [HttpGet("publishers")]
    public async Task<IActionResult> GetPublishers()
    {
        var publishers = await bookService.GetPublishers();
        return Ok(publishers);
    }

    [HttpGet("contributor-roles")]
    public IActionResult GetContributorRoles([FromQuery] string? mediaType)
    {
        if (string.Equals(mediaType, "Audiobook", StringComparison.OrdinalIgnoreCase))
            return Ok(new[] { "Narrator" });

        return Ok(EpubMetadataHandler.ContributorRoleNames);
    }
}
