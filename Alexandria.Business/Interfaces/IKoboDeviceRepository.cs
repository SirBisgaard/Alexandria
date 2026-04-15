using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface IKoboDeviceRepository
{
    Task Create(KoboDevice device);
    Task<KoboDevice?> GetByUuid(Guid deviceUuid);
    Task<List<KoboDevice>> GetAll();
    Task Update(KoboDevice device);
    Task Delete(Guid deviceUuid);
    Task UpdateLastSynced(Guid deviceUuid);
    Task SetLocked(Guid deviceUuid, bool locked);
    Task BindDevice(Guid deviceUuid, string serialNumber, string? modelName, string? modelCode, string? firmware);
}
