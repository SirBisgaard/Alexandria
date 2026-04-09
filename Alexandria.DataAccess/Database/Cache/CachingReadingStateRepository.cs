using System.Collections.Concurrent;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Cache;

public class CachingReadingStateRepository(IReadingStateRepository inner, CacheInvalidator cacheInvalidator) : IReadingStateRepository
{
    private readonly ConcurrentDictionary<Guid, KoboStoreReadingState?> stateCache = new();
    private readonly ConcurrentDictionary<Guid, ReadingProgress?> progressCache = new();

    public CachingReadingStateRepository Init()
    {
        cacheInvalidator.InvalidateReadingStateCache = Invalidate;
        return this;
    }

    public async Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid)
    {
        if (stateCache.TryGetValue(bookUuid, out var cached))
            return cached;

        var loaded = await inner.GetByBookUuid(bookUuid);
        stateCache[bookUuid] = loaded;
        return loaded;
    }

    public async Task<ReadingProgress?> GetReadingProgress(Guid bookUuid)
    {
        if (progressCache.TryGetValue(bookUuid, out var cached))
            return cached;

        var loaded = await inner.GetReadingProgress(bookUuid);
        progressCache[bookUuid] = loaded;
        return loaded;
    }

    public async Task Upsert(Guid bookUuid, KoboStoreReadingState state)
    {
        await inner.Upsert(bookUuid, state);
        stateCache.TryRemove(bookUuid, out _);
        progressCache.TryRemove(bookUuid, out _);
    }

    private void Invalidate()
    {
        stateCache.Clear();
        progressCache.Clear();
    }
}
