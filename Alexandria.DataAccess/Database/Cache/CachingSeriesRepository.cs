using System.Collections.Concurrent;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Cache;

public class CachingSeriesRepository(ISeriesRepository inner, CacheInvalidator cacheInvalidator) : ISeriesRepository
{
    private readonly RepositoryCache<Guid, Series> cache = new();
    private readonly ConcurrentDictionary<Guid, Series?> seriesByBookCache = new();

    public CachingSeriesRepository Init()
    {
        cacheInvalidator.InvalidateSeriesCache = Invalidate;
        return this;
    }

    public Task<List<Series>> GetAll()
    {
        return cache.GetOrLoadAll(inner.GetAll, s => s.SeriesUuid);
    }

    public Task<Series?> FindByUuid(Guid seriesUuid)
    {
        return cache.GetOrLoad(seriesUuid, () => inner.FindByUuid(seriesUuid));
    }

    public async Task<Guid> GetOrCreate(string name)
    {
        var result = await inner.GetOrCreate(name);
        cacheInvalidator.InvalidateSeries();
        return result;
    }

    public async Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index)
    {
        await inner.SetBookSeries(bookUuid, seriesUuid, index);
        cacheInvalidator.InvalidateSeries();
    }

    public async Task RemoveBookSeries(Guid bookUuid)
    {
        await inner.RemoveBookSeries(bookUuid);
        cacheInvalidator.InvalidateSeries();
    }

    public async Task<Series?> GetSeriesByBookUuid(Guid bookUuid)
    {
        if (seriesByBookCache.TryGetValue(bookUuid, out var cached))
            return cached;

        var loaded = await inner.GetSeriesByBookUuid(bookUuid);
        seriesByBookCache[bookUuid] = loaded;
        return loaded;
    }

    public async Task RemoveOrphanedSeries()
    {
        await inner.RemoveOrphanedSeries();
        cacheInvalidator.InvalidateSeries();
    }

    public async Task<Series?> FindByName(string name)
    {
        var all = await GetAll();
        var found = all.Find(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
            return found;

        return await inner.FindByName(name);
    }

    public async Task Update(Series series)
    {
        await inner.Update(series);
        cacheInvalidator.InvalidateSeries();
    }

    public async Task ReassignBooks(Guid sourceUuid, Guid targetUuid)
    {
        await inner.ReassignBooks(sourceUuid, targetUuid);
        cacheInvalidator.InvalidateSeries();
    }

    private void Invalidate()
    {
        cache.Invalidate();
        seriesByBookCache.Clear();
    }
}
