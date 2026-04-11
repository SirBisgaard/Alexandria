namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDeviceSyncRepository
{
    Task<Dictionary<Guid, DateTimeOffset>> GetSyncedModifiedDates(string deviceId);
    Task UpsertSyncedFile(string deviceId, Guid bookUuid, DateTimeOffset bookModifiedAt);
    Task RemoveSyncedFile(string deviceId, Guid bookUuid);
}
