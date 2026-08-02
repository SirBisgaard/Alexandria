using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface ISeriesRepository
{
    Task<List<Series>> GetAll();
    Task<Series?> FindByUuid(Guid seriesUuid);
    Task<Guid> GetOrCreate(string name);
    Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index);
    Task RemoveBookSeries(Guid bookUuid);
    Task<Series?> GetSeriesByBookUuid(Guid bookUuid);
    Task<Dictionary<Guid, Series>> GetSeriesForAllBooks();
    Task RemoveOrphanedSeries();
    Task<Series?> FindByName(string name);
    Task Update(Series series);
    Task ReassignBooks(Guid sourceUuid, Guid targetUuid);
}
