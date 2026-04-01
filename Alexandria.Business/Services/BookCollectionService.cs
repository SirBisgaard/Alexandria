using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class BookCollectionService(IBookCollectionRepository repository) : IBookCollectionService
{
    public async Task<List<BookCollection>> GetAll() => await repository.GetAll();

    public async Task<BookCollection?> GetByUuid(Guid uuid) => await repository.FindByUuid(uuid);

    public async Task<BookCollection> CreateOrUpdate(string name, List<Guid> bookUuids)
    {
        var uuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        var existing = await repository.FindByUuid(uuid);
        var now = DateTimeOffset.UtcNow;

        var collection = new BookCollection
        {
            Uuid = uuid,
            Name = name,
            BookUuids = bookUuids,
            CreatedAt = existing?.CreatedAt ?? now,
            LastModified = now
        };

        await repository.Upsert(collection);
        return collection;
    }

    public async Task Upsert(BookCollection collection) => await repository.Upsert(collection);

    public async Task Delete(Guid uuid) => await repository.Delete(uuid);

    public async Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since) => await repository.GetModifiedSince(since);
}
