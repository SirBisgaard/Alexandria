using System.Collections.Concurrent;
using Alexandria.Business.Interfaces;

namespace Alexandria.Business.Services;

public class SyncSessionService : ISyncSessionService
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, SyncSession> sessions = new();

    public bool IsActive(string deviceId)
    {
        if (!sessions.TryGetValue(deviceId, out var session))
        {
            return false;
        }

        if (DateTime.UtcNow - session.CreatedAt >= SessionTimeout)
        {
            sessions.TryRemove(deviceId, out _);
            return false;
        }

        return true;
    }

    public void Start(string deviceId, List<Guid> uuids)
    {
        sessions[deviceId] = new SyncSession(uuids, 0, DateTime.UtcNow);
    }

    public List<Guid> GetNextBatch(string deviceId, int batchSize)
    {
        if (!sessions.TryGetValue(deviceId, out var session))
        {
            return [];
        }

        var batch = session.BookUuids.Skip(session.Offset).Take(batchSize).ToList();
        sessions[deviceId] = session with { Offset = session.Offset + batch.Count };
        return batch;
    }

    public bool HasMore(string deviceId)
    {
        if (!sessions.TryGetValue(deviceId, out var session))
        {
            return false;
        }

        return session.Offset < session.BookUuids.Count;
    }

    public void Clear(string deviceId)
    {
        sessions.TryRemove(deviceId, out _);
    }

    private record SyncSession(List<Guid> BookUuids, int Offset, DateTime CreatedAt);
}
