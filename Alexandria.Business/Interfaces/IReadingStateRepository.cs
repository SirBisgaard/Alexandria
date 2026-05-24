using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface IReadingStateRepository
{
    Task<KoboStoreReadingState?> GetByBookUuid(Guid bookUuid);
    Task<ReadingProgress?> GetReadingProgress(Guid bookUuid);
    /// <summary>Inserts the row when none exists, otherwise updates only when <c>state.LastModified</c> is strictly newer than the stored value. Returns true when a row was written.</summary>
    Task<bool> Upsert(Guid bookUuid, KoboStoreReadingState state);
    Task DeleteByBookUuid(Guid bookUuid);
}
