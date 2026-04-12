using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models;
using Alexandria.Api.Models.Events;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers;

file class ChannelProgress<T>(Func<T, string> formatter, ChannelWriter<string> writer) : IProgress<T>
{
    public void Report(T value) => writer.TryWrite(formatter(value));
}

[ApiController]
[Route("/api/alexandria")]
public class AlexandriaController(IAlexandriaLibraryService alexandriaLibraryService, IAlexandriaSettingsService settingsService, ILogger<AlexandriaController> logger) : ControllerBase
{
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks([FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Recent, order ?? SortOrder.Desc);
        var books = await alexandriaLibraryService.GetBooks(options);
        return Ok(books.Select(ApiMapper.ToBookResponse));
    }

    [HttpGet("books/{uuid:guid}")]
    public async Task<IActionResult> GetBook(Guid uuid)
    {
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (book is null)
        {
            return NotFound();
        }
        return Ok(ApiMapper.ToBookResponse(book));
    }

    [HttpPut("books/{uuid:guid}/status")]
    public async Task<IActionResult> UpdateReadingStatus(Guid uuid, [FromBody] UpdateStatusRequest request)
    {
        logger.LogInformation("Updating reading status for {Uuid} to {Status}", uuid, request.Status);
        await alexandriaLibraryService.UpdateReadingStatus(uuid, request.Status);
        return Ok();
    }

    [HttpPut("books/{uuid:guid}/archive")]
    public async Task<IActionResult> UpdateArchiveStatus(Guid uuid, [FromBody] UpdateArchiveRequest request)
    {
        logger.LogInformation("Setting archive status for {Uuid} to {IsArchived}", uuid, request.IsArchived);
        var book = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (book is null)
        {
            return NotFound();
        }
        await alexandriaLibraryService.SetBookArchiveStatus(uuid, request.IsArchived);
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
        return Ok(authors.Select(ApiMapper.ToAuthorDetailResponse));
    }

    [HttpGet("authors/{uuid:guid}")]
    public async Task<IActionResult> GetAuthor(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.Title, order ?? SortOrder.Asc);
        var author = await alexandriaLibraryService.GetAuthorByUuid(uuid, options);
        if (author is null)
        {
            return NotFound();
        }
        return Ok(ApiMapper.ToAuthorDetailResponse(author));
    }

    [HttpGet("publishers")]
    public async Task<IActionResult> GetPublishers()
    {
        var publishers = await alexandriaLibraryService.GetPublishers();
        return Ok(publishers);
    }

    [HttpGet("series")]
    public async Task<IActionResult> GetSeries([FromQuery] SeriesSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new SeriesSortOptions(sort ?? SeriesSortField.SeriesTitle, order ?? SortOrder.Asc);
        var series = await alexandriaLibraryService.GetSeries(options);
        return Ok(series.Select(ApiMapper.ToSeriesDetailResponse));
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
        return Ok(ApiMapper.ToSeriesDetailResponse(series));
    }

    [HttpPut("authors/{uuid:guid}")]
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

    [HttpPut("series/{uuid:guid}")]
    public async Task<IActionResult> UpdateSeries(Guid uuid, [FromBody] UpdateSeriesRequest request)
    {
        logger.LogInformation("Updating series {Uuid}", uuid);
        var existing = await alexandriaLibraryService.GetSeriesByUuid(uuid);
        if (existing is null)
            return NotFound();

        var series = ApiMapper.ToSeries(request);
        series.SeriesUuid = uuid;
        if (!string.Equals(existing.Name, series.Name, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = await alexandriaLibraryService.GetSeriesByName(series.Name);
            if (conflict is not null)
            {
                var merged = await alexandriaLibraryService.MergeSeries(uuid, conflict.SeriesUuid);
                return Ok(ApiMapper.ToSeriesDetailResponse(merged!));
            }
        }

        var result = await alexandriaLibraryService.UpdateSeries(series);
        return Ok(ApiMapper.ToSeriesDetailResponse(result!));
    }

    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections([FromQuery] CollectionSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new CollectionSortOptions(sort ?? CollectionSortField.CollectionName, order ?? SortOrder.Asc);
        var collections = await alexandriaLibraryService.GetCollections(options);
        var books = await alexandriaLibraryService.GetBooks();
        var bookMediaTypes = books.ToDictionary(b => b.BookUuid, b => b.MediaType);
        return Ok(collections.Select(c => ApiMapper.ToCollectionResponse(c, bookMediaTypes)));
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
        return Ok(ApiMapper.ToCollectionDetailResponse(result));
    }

    [HttpPut("books/{uuid:guid}/metadata")]
    public async Task<IActionResult> UpdateBookMetadata(Guid uuid, [FromBody] UpdateBookMetadataRequest request)
    {
        logger.LogInformation("Updating metadata for book {Uuid}", uuid);
        var existing = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (existing is null)
            return NotFound();

        if (existing.MediaType == MediaType.Audiobook && request.Contributors is not null && request.Contributors.Any(c => c.Role is not null && !string.Equals(c.Role, "Narrator", StringComparison.OrdinalIgnoreCase)))
            return BadRequest("Audiobooks only support the Narrator contributor role.");

        var book = ApiMapper.ToBook(request);
        book.BookUuid = uuid;

        if (request.Contributors is null)
            book.Contributors = existing.Contributors;

        var result = await alexandriaLibraryService.UpdateBookMetadata(book);
        return Ok(ApiMapper.ToBookResponse(result!));
    }

    [HttpPut("books/{uuid:guid}/cover")]
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

        var existing = await alexandriaLibraryService.GetBookByUuid(uuid);
        if (existing is null)
        {
            return NotFound();
        }

        using var ms = new MemoryStream();
        await cover.CopyToAsync(ms);

        var result = await alexandriaLibraryService.UpdateBookCover(uuid, ms.ToArray(), cover.ContentType);
        return Ok(ApiMapper.ToBookResponse(result!));
    }

    [HttpDelete("books/{uuid:guid}/cover")]
    public async Task<IActionResult> RemoveBookCover(Guid uuid)
    {
        logger.LogInformation("Removing cover for book {Uuid}", uuid);
        var result = await alexandriaLibraryService.RemoveBookCover(uuid);
        if (result is null)
        {
            return NotFound();
        }
        return Ok(ApiMapper.ToBookResponse(result));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await settingsService.Get();
        return Ok(ApiMapper.ToSettingsResponse(settings));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        logger.LogInformation("Updating application settings");
        await settingsService.UpdateSettings(ApiMapper.ToAppSettings(request));
        return NoContent();
    }

    [HttpGet("contributor-roles")]
    public IActionResult GetContributorRoles([FromQuery] string? mediaType)
    {
        if (string.Equals(mediaType, "Audiobook", StringComparison.OrdinalIgnoreCase))
            return Ok(new[] { "Narrator" });

        return Ok(EpubMetadataHandler.ContributorRoleNames);
    }

    [HttpPost("sync")]
    public async Task Sync()
    {
        logger.LogInformation("Starting library sync");
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var channel = Channel.CreateUnbounded<string>();

        var progress = new ChannelProgress<(int current, int total)>(value =>
        {
            var json = JsonSerializer.Serialize(new SyncProgressEvent(value.current, value.total), jsonOptions);
            return $"data: {json}\n\n";
        }, channel.Writer);

        var syncTask = Task.Run(async () =>
        {
            try
            {
                var bookCount = await alexandriaLibraryService.Sync(progress);
                var settings = await settingsService.Get();
                var lastIndexed = settings.LastIndexedTimestamp?.ToString("O");
                var doneJson = JsonSerializer.Serialize(new SyncCompleteEvent(true, bookCount, lastIndexed), jsonOptions);
                channel.Writer.TryWrite($"data: {doneJson}\n\n");
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        await foreach (var message in channel.Reader.ReadAllAsync())
        {
            await Response.WriteAsync(message);
            await Response.Body.FlushAsync();
        }

        await syncTask;
    }
}
