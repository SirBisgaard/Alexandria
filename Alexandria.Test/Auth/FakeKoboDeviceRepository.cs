using System.Collections.Concurrent;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.Tests.Auth;

/// <summary>
/// In-memory stand-in for <see cref="IKoboDeviceRepository"/>. Supports the subset of behavior
/// needed by auth-related tests: upsert a device, look up by DeviceId, and update the token.
/// </summary>
internal sealed class FakeKoboDeviceRepository : IKoboDeviceRepository
{
    private readonly ConcurrentDictionary<string, KoboDevice> devices = new();

    public FakeKoboDeviceRepository(params string[] seedDeviceIds)
    {
        foreach (var id in seedDeviceIds)
        {
            devices[id] = new KoboDevice
            {
                DeviceUuid = Guid.NewGuid(),
                DeviceId = id,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow,
                LastRequestAt = DateTimeOffset.UtcNow
            };
        }
    }

    public Task Upsert(KoboDevice device)
    {
        devices[device.DeviceId] = device;
        return Task.CompletedTask;
    }

    public Task<KoboDevice?> GetByDeviceId(string deviceId)
    {
        return Task.FromResult(devices.TryGetValue(deviceId, out var d) ? d : null);
    }

    public Task<KoboDevice?> GetByUuid(Guid deviceUuid)
    {
        return Task.FromResult<KoboDevice?>(devices.Values.FirstOrDefault(d => d.DeviceUuid == deviceUuid));
    }

    public Task<List<KoboDevice>> GetAll() => Task.FromResult(devices.Values.ToList());

    public Task Update(KoboDevice device)
    {
        devices[device.DeviceId] = device;
        return Task.CompletedTask;
    }

    public Task Delete(Guid deviceUuid)
    {
        var match = devices.Values.FirstOrDefault(d => d.DeviceUuid == deviceUuid);
        if (match is not null)
            devices.TryRemove(match.DeviceId, out _);
        return Task.CompletedTask;
    }

    public Task UpdateLastRequest(string deviceId)
    {
        if (devices.TryGetValue(deviceId, out var d))
            d.LastRequestAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task UpdateDeviceToken(string deviceId, string token)
    {
        if (devices.TryGetValue(deviceId, out var d))
            d.DeviceToken = token;
        return Task.CompletedTask;
    }
}
