using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDeviceSyncTokenService
{
    KoboDeviceSyncToken ParseToken(string? headerValue);
    string GenerateToken(KoboDeviceSyncToken token);
    bool IsActive(string deviceId);
    void Start(string deviceId, List<Guid> uuids);
    List<Guid> GetNextBatch(string deviceId, int batchSize);
    bool HasMore(string deviceId);
    void Clear(string deviceId);
}
