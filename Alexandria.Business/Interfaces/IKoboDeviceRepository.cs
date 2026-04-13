using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface IKoboDeviceRepository
{
    Task Upsert(KoboDevice device);
    Task<KoboDevice?> GetByDeviceId(string deviceId);
    Task<KoboDevice?> GetByUuid(Guid deviceUuid);
    Task<List<KoboDevice>> GetAll();
    Task Update(KoboDevice device);
    Task Delete(Guid deviceUuid);
    Task UpdateLastRequest(string deviceId);
    Task UpdateDeviceToken(string deviceId, string token);
}
