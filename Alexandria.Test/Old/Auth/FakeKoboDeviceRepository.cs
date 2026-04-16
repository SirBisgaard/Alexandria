using System.Collections.Concurrent;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.Tests.Auth;

/// <summary>
/// In-memory stand-in for <see cref="IKoboDeviceRepository"/>. Supports the subset of behavior
/// needed by tests: create a device, look up by UUID, bind serial, and lock/unlock.
/// </summary>
internal sealed class FakeKoboDeviceRepository : IKoboDeviceRepository
{
    private readonly ConcurrentDictionary<Guid, KoboDevice> devices = new();

    public FakeKoboDeviceRepository(params KoboDevice[] seedDevices)
    {
        foreach (var device in seedDevices)
            devices[device.DeviceUuid] = device;
    }

    public Task Create(KoboDevice device)
    {
        devices[device.DeviceUuid] = device;
        return Task.CompletedTask;
    }

    public Task<KoboDevice?> GetByUuid(Guid deviceUuid)
    {
        return Task.FromResult(devices.TryGetValue(deviceUuid, out var d) ? d : null);
    }

    public Task<List<KoboDevice>> GetAll() => Task.FromResult(devices.Values.ToList());

    public Task Update(KoboDevice device)
    {
        devices[device.DeviceUuid] = device;
        return Task.CompletedTask;
    }

    public Task Delete(Guid deviceUuid)
    {
        devices.TryRemove(deviceUuid, out _);
        return Task.CompletedTask;
    }

    public Task UpdateLastSynced(Guid deviceUuid)
    {
        if (devices.TryGetValue(deviceUuid, out var d))
            d.LastSyncedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task SetLocked(Guid deviceUuid, bool locked)
    {
        if (devices.TryGetValue(deviceUuid, out var d))
            d.IsLocked = locked;
        return Task.CompletedTask;
    }

    public Task BindDevice(Guid deviceUuid, string serialNumber, string? modelName, string? modelCode, string? firmware)
    {
        if (devices.TryGetValue(deviceUuid, out var d))
        {
            d.SerialNumber = serialNumber;
            d.ModelName = modelName ?? d.ModelName;
            d.ModelCode = modelCode ?? d.ModelCode;
            d.Firmware = firmware ?? d.Firmware;
        }
        return Task.CompletedTask;
    }
}
