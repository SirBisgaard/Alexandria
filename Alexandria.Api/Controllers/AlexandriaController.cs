using Alexandria.Business.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

/// <summary>
/// Local management API for the Alexandria library. Not accessed by Kobo devices —
/// intended for web UIs or other clients managing the book collection.
/// </summary>
[ApiController]
[Route("alexandria/api")]
public class AlexandriaController(IAlexandriaLibraryService alexandriaLibraryService) : ControllerBase
{
    /// <summary>
    /// Returns all books in the Alexandria library, including archived books.
    /// </summary>
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks()
    {
        var books = await alexandriaLibraryService.GetBooks();
        return Ok(books);
    }
}
