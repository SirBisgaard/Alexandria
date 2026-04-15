using Alexandria.Api.Models;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/authors")]
public class AuthorsController(IAlexandriaLibraryService alexandriaLibraryService, ILogger<AuthorsController> logger) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAuthors([FromQuery] AuthorSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new AuthorSortOptions(sort ?? AuthorSortField.LastName, order ?? SortOrder.Asc);
        var authors = await alexandriaLibraryService.GetAuthors(options);
        return Ok(authors.Select(ApiMapper.ToAuthorDetailResponse));
    }

    [HttpGet("{uuid:guid}")]
    public async Task<IActionResult> GetAuthor(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Title, order ?? SortOrder.Asc);
        var author = await alexandriaLibraryService.GetAuthorByUuid(uuid, options);
        if (author is null)
            return NotFound();
        return Ok(ApiMapper.ToAuthorDetailResponse(author));
    }

    [HttpPut("{uuid:guid}")]
    public async Task<IActionResult> UpdateAuthor(Guid uuid, [FromBody] UpdateAuthorRequest request)
    {
        logger.LogInformation("Updating author {Uuid}", uuid);
        var existing = await alexandriaLibraryService.GetAuthorByUuid(uuid);
        if (existing is null)
            return NotFound();

        var author = ApiMapper.ToAuthor(request);
        author.AuthorUuid = uuid;
        if (!string.Equals(existing.Name, author.Name, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = await alexandriaLibraryService.GetAuthorByName(author.Name);
            if (conflict is not null)
            {
                var merged = await alexandriaLibraryService.MergeAuthor(uuid, conflict.AuthorUuid);
                return Ok(ApiMapper.ToAuthorDetailResponse(merged!));
            }
        }

        var result = await alexandriaLibraryService.UpdateAuthor(author);
        return Ok(ApiMapper.ToAuthorDetailResponse(result!));
    }
}
