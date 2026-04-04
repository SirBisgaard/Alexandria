using Alexandria.Api.Models;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

[ApiController]
[Route("/api/alexandria")]
public class AlexandriaController(IAlexandriaLibraryService alexandriaLibraryService) : ControllerBase
{
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks([FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Recent, order ?? SortOrder.Desc);
        var books = await alexandriaLibraryService.GetBooks(options);
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

    [HttpPut("books/{uuid:guid}/status")]
    public async Task<IActionResult> UpdateReadingStatus(Guid uuid, [FromBody] UpdateStatusRequest request)
    {
        await alexandriaLibraryService.UpdateReadingStatus(uuid, request.Status);
        return Ok();
    }

    [HttpGet("books/{uuid:guid}/cover")]
    public async Task<IActionResult> GetBookCover(Guid uuid, [FromQuery] int? width = null, [FromQuery] int? height = null)
    {
        var cover = await alexandriaLibraryService.GetBookCover(uuid, width, height);
        if (cover is null)
        {
            return NotFound();
        }
        return File(cover, "image/jpeg");
    }

    [HttpGet("authors")]
    public async Task<IActionResult> GetAuthors([FromQuery] AuthorSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new AuthorSortOptions(sort ?? AuthorSortField.LastName, order ?? SortOrder.Asc);
        var authors = await alexandriaLibraryService.GetAuthors(options);
        return Ok(authors);
    }

    [HttpGet("authors/{name}")]
    public async Task<IActionResult> GetAuthor(string name, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Title, order ?? SortOrder.Asc);
        var author = await alexandriaLibraryService.GetAuthorByName(name, options);
        if (author is null)
        {
            return NotFound();
        }
        return Ok(author);
    }

    [HttpGet("series")]
    public async Task<IActionResult> GetSeries([FromQuery] SeriesSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new SeriesSortOptions(sort ?? SeriesSortField.SeriesTitle, order ?? SortOrder.Asc);
        var series = await alexandriaLibraryService.GetSeries(options);
        return Ok(series);
    }

    [HttpGet("series/{uuid:guid}")]
    public async Task<IActionResult> GetSeriesByUuid(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.BookNumber, order ?? SortOrder.Asc);
        var series = await alexandriaLibraryService.GetSeriesByUuid(uuid, options);
        if (series is null)
        {
            return NotFound();
        }
        return Ok(series);
    }

    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections([FromQuery] CollectionSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new CollectionSortOptions(sort ?? CollectionSortField.CollectionName, order ?? SortOrder.Asc);
        var collections = await alexandriaLibraryService.GetCollections(options);
        return Ok(collections);
    }

    [HttpGet("collections/{uuid:guid}")]
    public async Task<IActionResult> GetCollection(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Title, order ?? SortOrder.Asc);
        var result = await alexandriaLibraryService.GetCollectionWithBooks(uuid, options);
        if (result is null)
        {
            return NotFound();
        }
        return Ok(result);
    }
    
    [HttpPut("books/{uuid:guid}/metadata")]
    public async Task<IActionResult> UpdateBookMetadata(Guid uuid, [FromBody] Book book)
    {
        var existing = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (existing is null)
        {
            return NotFound();
        }
        
        var result = await alexandriaLibraryService.UpdateBookMetadata(uuid, book);
        return Ok(result);
    }

    [HttpPut("books/{uuid:guid}/cover")]
    public async Task<IActionResult> UpdateBookCover(Guid uuid, IFormFile? cover)
    {
        if (cover is null || cover.Length == 0)
        {
            return BadRequest("No cover file provided.");
        }

        var existing = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (existing is null)
        {
            return NotFound();
        }

        using var ms = new MemoryStream();
        await cover.CopyToAsync(ms);

        var result = await alexandriaLibraryService.UpdateBookCover(uuid, ms.ToArray(), cover.ContentType);
        return Ok(result);
    }
}