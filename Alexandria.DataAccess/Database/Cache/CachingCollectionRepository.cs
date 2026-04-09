using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Cache;

public class CachingCollectionRepository(ICollectionRepository inner, CacheInvalidator cacheInvalidator) : ICollectionRepository
{
    private readonly RepositoryCache<Guid, BookCollection> cache = new();

    public CachingCollectionRepository Init()
    {
        cacheInvalidator.InvalidateCollectionCache = cache.Invalidate;
        return this;
    }

    public Task<List<BookCollection>> GetAll()
    {
        return cache.GetOrLoadAll(inner.GetAll, c => c.CollectionUuid);
    }

    public Task<BookCollection?> FindByUuid(Guid uuid)
    {
        return cache.GetOrLoad(uuid, () => inner.FindByUuid(uuid));
    }

    public async Task Upsert(BookCollection collection)
    {
        await inner.Upsert(collection);
        cacheInvalidator.InvalidateCollections();
    }

    public async Task Delete(Guid uuid)
    {
        await inner.Delete(uuid);
        cacheInvalidator.InvalidateCollections();
    }

    public Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since)
    {
        return inner.GetModifiedSince(since);
    }
}
