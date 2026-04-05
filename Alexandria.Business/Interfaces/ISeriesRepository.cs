using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface ISeriesRepository
{
    Task<List<Series>> GetAll();
    Task<Series?> FindByUuid(Guid seriesUuid);
    Task<Series?> FindByName(string name);
    Task<Guid> GetOrCreate(string name);
    Task SetBookSeries(Guid bookUuid, Guid seriesUuid, double? index);
    Task RemoveBookSeries(Guid bookUuid);
    Task<Series?> GetSeriesByBookUuid(Guid bookUuid);
    Task RemoveOrphanedSeries();
}
