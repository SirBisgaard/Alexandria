using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface IKoboDeviceRepository
{
    Task Upsert(KoboDevice device);
    Task<KoboDevice?> GetByDeviceId(string deviceId);
    Task<List<KoboDevice>> GetAll();
}
