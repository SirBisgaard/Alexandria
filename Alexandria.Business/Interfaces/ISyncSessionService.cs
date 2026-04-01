namespace Alexandria.Business.Interfaces;

public interface ISyncSessionService
{
    bool IsActive(string deviceId);
    void Start(string deviceId, List<Guid> uuids);
    List<Guid> GetNextBatch(string deviceId, int batchSize);
    bool HasMore(string deviceId);
    void Clear(string deviceId);
}
