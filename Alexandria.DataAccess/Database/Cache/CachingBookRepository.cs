using System.Collections.Concurrent;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Cache;

public class CachingBookRepository(IBookRepository inner, CacheInvalidator cacheInvalidator) : IBookRepository
{
    private readonly RepositoryCache<Guid, Book> cache = new();
    private readonly ConcurrentDictionary<Guid, Book> byEntitlementUuid = new();
    private readonly ConcurrentDictionary<Guid, Book> byCoverImageUuid = new();
    private readonly ConcurrentDictionary<string, Book> byHash = new();
    private readonly ConcurrentDictionary<string, Book> byPath = new(StringComparer.OrdinalIgnoreCase);
    private volatile List<string>? publishersCache;

    public CachingBookRepository Init()
    {
        cacheInvalidator.InvalidateBookCache = Invalidate;
        return this;
    }

    public async Task<List<Book>> GetAll()
    {
        return await cache.GetOrLoadAll(async () =>
        {
            var books = await inner.GetAll();
            RebuildSecondaryIndexes(books);
            return books;
        }, b => b.BookUuid);
    }

    public Task<Book?> FindByUuid(Guid uuid)
    {
        return cache.GetOrLoad(uuid, () => inner.FindByUuid(uuid));
    }

    public async Task<Book?> FindByEntitlementUuid(Guid entitlementUuid)
    {
        if (byEntitlementUuid.TryGetValue(entitlementUuid, out var cached))
            return cached;

        var loaded = await inner.FindByEntitlementUuid(entitlementUuid);
        if (loaded is not null)
            byEntitlementUuid[entitlementUuid] = loaded;

        return loaded;
    }

    public async Task<Book?> FindByCoverImageUuid(Guid uuid)
    {
        if (byCoverImageUuid.TryGetValue(uuid, out var cached))
            return cached;

        var loaded = await inner.FindByCoverImageUuid(uuid);
        if (loaded is not null)
            byCoverImageUuid[uuid] = loaded;

        return loaded;
    }

    public async Task<Book?> FindByHash(string fileHash)
    {
        if (byHash.TryGetValue(fileHash, out var cached))
            return cached;

        var loaded = await inner.FindByHash(fileHash);
        if (loaded is not null)
            byHash[fileHash] = loaded;

        return loaded;
    }

    public async Task<Book?> FindByPath(string sourcePath)
    {
        if (byPath.TryGetValue(sourcePath, out var cached))
            return cached;

        var loaded = await inner.FindByPath(sourcePath);
        if (loaded is not null)
            byPath[sourcePath] = loaded;

        return loaded;
    }

    public async Task Add(Book book)
    {
        await inner.Add(book);
        cacheInvalidator.InvalidateBooks();
    }

    public async Task Update(Book book)
    {
        await inner.Update(book);
        cacheInvalidator.InvalidateBooks();
    }

    public async Task Remove(Guid uuid)
    {
        await inner.Remove(uuid);
        cacheInvalidator.InvalidateBooks();
    }

    public async Task<List<string>> GetPublishers()
    {
        var cached = publishersCache;
        if (cached is not null)
            return cached;

        var loaded = await inner.GetPublishers();
        publishersCache = loaded;
        return loaded;
    }

    private void RebuildSecondaryIndexes(List<Book> books)
    {
        byEntitlementUuid.Clear();
        byCoverImageUuid.Clear();
        byHash.Clear();
        byPath.Clear();

        foreach (var book in books)
        {
            byEntitlementUuid[book.EntitlementUuid] = book;
            if (book.CoverImageUuid.HasValue)
                byCoverImageUuid[book.CoverImageUuid.Value] = book;
            if (!string.IsNullOrEmpty(book.FileHash))
                byHash[book.FileHash] = book;
            if (!string.IsNullOrEmpty(book.SourcePath))
                byPath[book.SourcePath] = book;
        }
    }

    private void Invalidate()
    {
        cache.Invalidate();
        byEntitlementUuid.Clear();
        byCoverImageUuid.Clear();
        byHash.Clear();
        byPath.Clear();
        publishersCache = null;
    }
}
