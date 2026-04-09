using System.Collections.Concurrent;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Cache;

public class CachingAuthorRepository(IAuthorRepository inner, CacheInvalidator cacheInvalidator) : IAuthorRepository
{
    private readonly RepositoryCache<Guid, Author> cache = new();
    private readonly ConcurrentDictionary<Guid, List<Author>> authorsByBookCache = new();

    public CachingAuthorRepository Init()
    {
        cacheInvalidator.InvalidateAuthorCache = Invalidate;
        return this;
    }

    public Task<List<Author>> GetAll()
    {
        return cache.GetOrLoadAll(inner.GetAll, a => a.AuthorUuid);
    }

    public Task<Author?> FindByUuid(Guid authorUuid)
    {
        return cache.GetOrLoad(authorUuid, () => inner.FindByUuid(authorUuid));
    }

    public async Task<Author?> FindByName(string name)
    {
        var all = await GetAll();
        var found = all.Find(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
            return found;

        return await inner.FindByName(name);
    }

    public async Task<Guid> GetOrCreate(string name)
    {
        var result = await inner.GetOrCreate(name);
        cacheInvalidator.InvalidateAuthors();
        return result;
    }

    public async Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
    {
        await inner.SetBookAuthors(bookUuid, authorNames);
        cacheInvalidator.InvalidateAuthors();
    }

    public async Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid)
    {
        if (authorsByBookCache.TryGetValue(bookUuid, out var cached))
            return cached;

        var loaded = await inner.GetAuthorsByBookUuid(bookUuid);
        authorsByBookCache[bookUuid] = loaded;
        return loaded;
    }

    public async Task RemoveOrphanedAuthors()
    {
        await inner.RemoveOrphanedAuthors();
        cacheInvalidator.InvalidateAuthors();
    }

    public async Task Update(Author author)
    {
        await inner.Update(author);
        cacheInvalidator.InvalidateAuthors();
    }

    public async Task ReassignBooks(Guid sourceUuid, Guid targetUuid)
    {
        await inner.ReassignBooks(sourceUuid, targetUuid);
        cacheInvalidator.InvalidateAuthors();
    }

    private void Invalidate()
    {
        cache.Invalidate();
        authorsByBookCache.Clear();
    }
}
