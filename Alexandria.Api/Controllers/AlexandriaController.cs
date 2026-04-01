using Alexandria.Business.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("alexandria/api")]
public class AlexandriaController(IAlexandriaLibraryService alexandriaLibraryService) : ControllerBase
{
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks()
    {
        var books = await alexandriaLibraryService.GetBooksAsync();
        return Ok(books);
    }
}
