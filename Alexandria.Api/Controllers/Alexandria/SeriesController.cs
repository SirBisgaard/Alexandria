using Alexandria.Api.Models;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.AspNetCore.Mvc;

namespace Alexandria.Api.Controllers.Alexandria;

[Route("/api/alexandria/series")]
public class SeriesController(IAlexandriaLibraryService alexandriaLibraryService, ILogger<SeriesController> logger) : AlexandriaControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSeries([FromQuery] SeriesSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new SeriesSortOptions(sort ?? SeriesSortField.SeriesTitle, order ?? SortOrder.Asc);
        var series = await alexandriaLibraryService.GetSeries(options);
        return Ok(series.Select(ApiMapper.ToSeriesDetailResponse));
    }

    [HttpGet("{uuid:guid}")]
    public async Task<IActionResult> GetSeriesByUuid(Guid uuid, [FromQuery] BookSortField? sort, [FromQuery] SortOrder? order)
    {
        var options = new BookSortOptions(sort ?? BookSortField.BookNumber, order ?? SortOrder.Asc);
        var series = await alexandriaLibraryService.GetSeriesByUuid(uuid, options);
        if (series is null)
            return NotFound();
        return Ok(ApiMapper.ToSeriesDetailResponse(series));
    }

    [HttpPut("{uuid:guid}")]
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
}
