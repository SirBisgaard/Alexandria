using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Services;

public class KoboDeviceSyncTokenService : IKoboDeviceSyncTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, SyncSession> sessions = new();

    public KoboDeviceSyncToken ParseToken(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return new KoboDeviceSyncToken();
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
            var token = JsonSerializer.Deserialize<KoboDeviceSyncToken>(json, JsonOptions);
            if (token is not null && token.V >= 1)
            {
                return token;
            }
        }
        catch
        {
            // Not our token format — treat as raw Kobo store token (first sync or factory reset)
        }

        return new KoboDeviceSyncToken { KoboToken = headerValue };
    }

    public string GenerateToken(KoboDeviceSyncToken token)
    {
        var json = JsonSerializer.Serialize(token, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool IsActive(string deviceId)
    {
        if (!sessions.TryGetValue(deviceId, out var session))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - session.CreatedAt >= SessionTimeout)
        {
            sessions.TryRemove(deviceId, out _);
            return false;
        }

        return true;
    }

    public void Start(string deviceId, List<Guid> uuids)
    {
        sessions[deviceId] = new SyncSession(uuids, 0, DateTimeOffset.UtcNow);
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

    private record SyncSession(List<Guid> BookUuids, int Offset, DateTimeOffset CreatedAt);
}
