using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.Business.Auth;

/// <summary>
/// Source of truth for the current Kobo device token is the <c>KoboDevices.DeviceToken</c> column.
/// <see cref="Rotate"/> mints a fresh token and persists it — called only by <c>/v1/initialization</c>
/// so the device's cached <c>image_url_template</c> picks up the new value on the next re-init.
/// <see cref="GetOrMint"/> returns the stored token and mints+persists one on first contact for a
/// device (pre-existing rows without a token get populated lazily).
/// </summary>
public sealed class DeviceTokenStore(IKoboDeviceRepository deviceRepository, AuthSecretProvider authSecrets)
{
    public async Task<string> GetOrMint(string deviceId)
    {
        EnsureRealDeviceId(deviceId);
        var device = await deviceRepository.GetByDeviceId(deviceId);
        if (device is { DeviceToken.Length: > 0 })
            return device.DeviceToken;

        var token = Mint(deviceId);
        await deviceRepository.UpdateDeviceToken(deviceId, token);
        return token;
    }

    public async Task<string> Rotate(string deviceId)
    {
        EnsureRealDeviceId(deviceId);
        var token = Mint(deviceId);
        await deviceRepository.UpdateDeviceToken(deviceId, token);
        return token;
    }

    /// <summary>
    /// Refuses to operate on the placeholder id the sync service uses when no
    /// <c>x-kobo-deviceid</c> header is present. Prevents tokens keyed to the string literal
    /// <c>"unknown"</c> from ever being minted or persisted — if that slips into a URL the
    /// auth handler would accept it as a syntactically valid token but fail to resolve a device.
    /// </summary>
    private static void EnsureRealDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.Equals(deviceId, "unknown", StringComparison.Ordinal))
            throw new InvalidOperationException("DeviceTokenStore requires a real device id; caller must check for x-kobo-deviceid before invoking.");
    }

    private string Mint(string deviceId) => TokenHelpers.Mint(deviceId, authSecrets.GetDeviceHmacKey(), TokenHelpers.DeviceTokenTtl);
}
