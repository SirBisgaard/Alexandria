using System.Collections.Concurrent;

namespace Alexandria.DataAccess.Database.Cache;

public class RepositoryCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> items = new();
    private volatile List<TValue>? allItemsCache;
    private readonly SemaphoreSlim loadLock = new(1, 1);

    public async Task<List<TValue>> GetOrLoadAll(Func<Task<List<TValue>>> loader, Func<TValue, TKey> keySelector)
    {
        var cached = allItemsCache;
        if (cached is not null)
            return cached;

        await loadLock.WaitAsync();
        try
        {
            cached = allItemsCache;
            if (cached is not null)
                return cached;

            var loaded = await loader();
            items.Clear();
            foreach (var item in loaded)
                items[keySelector(item)] = item;

            allItemsCache = loaded;
            return loaded;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task<TValue?> GetOrLoad(TKey key, Func<Task<TValue?>> loader)
    {
        if (items.TryGetValue(key, out var cached))
            return cached;

        var loaded = await loader();
        if (loaded is not null)
            items[key] = loaded;

        return loaded;
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        if (items.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = default;
        return false;
    }

    public void Invalidate()
    {
        allItemsCache = null;
        items.Clear();
    }
}
