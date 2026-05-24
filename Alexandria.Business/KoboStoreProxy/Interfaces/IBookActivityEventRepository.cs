using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IBookActivityEventRepository
{
    Task Insert(BookActivityEvent activityEvent);
    Task<List<BookActivityEvent>> GetByBookUuid(Guid bookUuid);
    Task<int> DeleteAll();
}
