namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDeviceSyncRepository
{
    Task<Dictionary<Guid, string>> GetSyncedFileHashes(string deviceId);
    Task UpsertSyncedFile(string deviceId, Guid bookUuid, string fileHash);
    Task RemoveSyncedFile(string deviceId, Guid bookUuid);
}
