using Alexandria.Business.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("/api/alexandria")]
public class AlexandriaController(IAlexandriaLibraryService alexandriaLibraryService) : ControllerBase
{
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks()
    {
        var books = await alexandriaLibraryService.GetBooks();
        return Ok(books);
    }

    [HttpGet("books/{uuid:guid}")]
    public async Task<IActionResult> GetBook(Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (book is null)
        {
            return NotFound();
        }
        return Ok(book);
    }

    [HttpGet("books/{uuid:guid}/cover")]
    public async Task<IActionResult> GetBookCover(Guid uuid)
    {
        var cover = await alexandriaLibraryService.GetBookCover(uuid);
        if (cover is null)
        {
            return NotFound();
        }
        return File(cover, "image/jpeg");
    }

    [HttpGet("authors")]
    public async Task<IActionResult> GetAuthors()
    {
        var authors = await alexandriaLibraryService.GetAuthors();
        return Ok(authors);
    }

    [HttpGet("authors/{name}")]
    public async Task<IActionResult> GetAuthor(string name)
    {
        var author = await alexandriaLibraryService.GetAuthorByName(name);
        if (author is null)
        {
            return NotFound();
        }
        return Ok(author);
    }

    [HttpGet("series")]
    public async Task<IActionResult> GetSeries()
    {
        var series = await alexandriaLibraryService.GetSeries();
        return Ok(series);
    }

    [HttpGet("series/{uuid:guid}")]
    public async Task<IActionResult> GetSeriesByUuid(Guid uuid)
    {
        var series = await alexandriaLibraryService.GetSeriesByUuid(uuid);
        if (series is null)
        {
            return NotFound();
        }
        return Ok(series);
    }

    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections()
    {
        var collections = await alexandriaLibraryService.GetCollections();
        return Ok(collections);
    }

    [HttpGet("collections/{uuid:guid}")]
    public async Task<IActionResult> GetCollection(Guid uuid)
    {
        var collection = await alexandriaLibraryService.GetCollectionByUuid(uuid);
        if (collection is null)
        {
            return NotFound();
        }
        return Ok(collection);
    }
}
