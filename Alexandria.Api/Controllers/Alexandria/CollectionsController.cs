using Alexandria.Api.Models;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/collections")]
public class CollectionsController(ICollectionService collectionService, IBookService bookService) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCollections([FromQuery] CollectionSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new CollectionSortOptions(sort ?? CollectionSortField.CollectionName, order ?? SortOrder.Asc);
        var collections = await collectionService.GetCollections(options);
        var books = await bookService.GetBooks();
        var bookMediaTypes = books.ToDictionary(b => b.BookUuid, b => b.MediaType);
        return Ok(collections.Select(c => ApiMapper.ToCollectionResponse(c, bookMediaTypes)));
    }

    [HttpGet("{uuid:guid}")]
    public async Task<IActionResult> GetCollection(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Title, order ?? SortOrder.Asc);
        var result = await collectionService.GetCollectionWithBooks(uuid, options);
        if (result is null)
            return NotFound();
        return Ok(ApiMapper.ToCollectionDetailResponse(result));
    }
}
