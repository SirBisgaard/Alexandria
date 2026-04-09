using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Library.Interfaces;

public interface IReadingStateRepository
{
    Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid);
    Task<ReadingProgress?> GetReadingProgress(Guid bookUuid);
    Task Upsert(Guid bookUuid, KoboStoreReadingState state);
}
