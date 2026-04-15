using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface ISeriesService
{
    Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null);
    Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null);
    Task<Series?> GetSeriesByName(string name);
    Task<Series?> UpdateSeries(Series series);
    Task<Series?> MergeSeries(Guid sourceUuid, Guid targetUuid);
}
