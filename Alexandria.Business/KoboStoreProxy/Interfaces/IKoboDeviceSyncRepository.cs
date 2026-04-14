namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDeviceSyncRepository
{
    Task<Dictionary<Guid, DateTimeOffset>> GetSyncedModifiedDates(string deviceUuid);
    Task UpsertSyncedFile(string deviceUuid, Guid bookUuid, DateTimeOffset bookModifiedAt);
    Task RemoveSyncedFile(string deviceUuid, Guid bookUuid);
}
