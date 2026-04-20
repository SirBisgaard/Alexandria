using Alexandria.Api.Models;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/books")]
public class BooksController(IBookService bookService, ILogger<BooksController> logger) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBooks([FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Recent, order ?? SortOrder.Desc);
        var books = await bookService.GetBooks(options);
        return Ok(books.Select(ApiMapper.ToBookResponse));
    }

    [HttpGet("{uuid:guid}")]
    public async Task<IActionResult> GetBook(Guid uuid)
    {
        var book = await bookService.GetBookByUuid(uuid);
        if (book is null)
            return NotFound();
        return Ok(ApiMapper.ToBookResponse(book));
    }

    [HttpPut("{uuid:guid}/status")]
    public async Task<IActionResult> UpdateReadingStatus(Guid uuid, [FromBody] UpdateStatusRequest request)
    {
        logger.LogInformation("Updating reading status for {Uuid} to {Status}", uuid, request.Status);
        await bookService.UpdateReadingStatus(uuid, request.Status);
        return Ok();
    }

    [HttpPut("{uuid:guid}/archive")]
    public async Task<IActionResult> UpdateArchiveStatus(Guid uuid, [FromBody] UpdateArchiveRequest request)
    {
        logger.LogInformation("Setting archive status for {Uuid} to {IsArchived}", uuid, request.IsArchived);
        var book = await bookService.GetBookByUuid(uuid);
        if (book is null)
            return NotFound();
        await bookService.SetBookArchiveStatus(uuid, request.IsArchived);
        return Ok();
    }

    [HttpGet("{uuid:guid}/cover")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBookCover(Guid uuid, [FromQuery] int? width = null, [FromQuery] int? height = null)
    {
        var cover = await bookService.GetBookCover(uuid, width, height);
        if (cover is null)
            return NotFound();
        Response.Headers.CacheControl = "public, max-age=604800, immutable";
        return File(cover, "image/jpeg");
    }

    [HttpPut("{uuid:guid}/metadata")]
    public async Task<IActionResult> UpdateBookMetadata(Guid uuid, [FromBody] UpdateBookMetadataRequest request)
    {
        logger.LogInformation("Updating metadata for book {Uuid}", uuid);
        var existing = await bookService.GetBookByUuid(uuid);
        if (existing is null)
            return NotFound();

        if (existing.File.MediaType == MediaType.Audiobook && request.Contributors is not null && request.Contributors.Any(c => c.Role is not null && !string.Equals(c.Role, "Narrator", StringComparison.OrdinalIgnoreCase)))
            return BadRequest("Audiobooks only support the Narrator contributor role.");

        var book = ApiMapper.ToBook(request);
        book.BookUuid = uuid;

        if (request.Contributors is null)
            book.Contributors = existing.Contributors;

        var result = await bookService.UpdateBookMetadata(book);
        return Ok(ApiMapper.ToBookResponse(result!));
    }

    [HttpPut("{uuid:guid}/cover")]
    public async Task<IActionResult> UpdateBookCover(Guid uuid, IFormFile? cover)
    {
        logger.LogInformation("Updating cover for book {Uuid}", uuid);
        if (cover is null || cover.Length == 0)
            return BadRequest("No cover file provided.");

        string[] allowedTypes = ["image/jpeg", "image/png", "image/gif"];
        if (!allowedTypes.Contains(cover.ContentType))
            return BadRequest("Cover must be a JPEG, PNG, or GIF image.");

        if (cover.Length > 10 * 1024 * 1024)
            return BadRequest("Cover file must be smaller than 10 MB.");

        var existing = await bookService.GetBookByUuid(uuid);
        if (existing is null)
            return NotFound();

        using var ms = new MemoryStream();
        await cover.CopyToAsync(ms);

        var result = await bookService.UpdateBookCover(uuid, ms.ToArray(), cover.ContentType);
        return Ok(ApiMapper.ToBookResponse(result!));
    }

    [HttpDelete("{uuid:guid}/cover")]
    public async Task<IActionResult> RemoveBookCover(Guid uuid)
    {
        logger.LogInformation("Removing cover for book {Uuid}", uuid);
        var result = await bookService.RemoveBookCover(uuid);
        if (result is null)
            return NotFound();
        return Ok(ApiMapper.ToBookResponse(result));
    }
}
