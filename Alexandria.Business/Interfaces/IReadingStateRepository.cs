using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;

namespace Alexandria.Business.Interfaces;

public interface IReadingStateRepository
{
    Task<ReadingState?> GetByBookUuid(Guid bookUuid);
    Task<ReadingProgress?> GetReadingProgress(Guid bookUuid);
    Task Upsert(Guid bookUuid, ReadingState state, string rawPutBody);
}
