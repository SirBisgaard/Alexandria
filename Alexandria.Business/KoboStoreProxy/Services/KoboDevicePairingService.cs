using Alexandria.Business.KoboStoreProxy.Interfaces;

namespace Alexandria.Business.KoboStoreProxy.Services;

/// <summary>
/// Controls whether new Kobo devices can pair with the server.
/// Pairing is disabled by default; an admin enables it for a short window (typically 5 minutes)
/// during which unknown devices are allowed to register.
/// In-memory only — a server restart disables pairing, which is the safe default.
/// </summary>
public class KoboDevicePairingService : IKoboDevicePairingService
{
    private DateTimeOffset? pairingExpiresAt;

    public bool IsPairingEnabled()
    {
        return pairingExpiresAt is not null && DateTimeOffset.UtcNow < pairingExpiresAt;
    }

    public DateTimeOffset? GetPairingExpiry()
    {
        if (!IsPairingEnabled())
            return null;

        return pairingExpiresAt;
    }

    public void EnablePairing(TimeSpan duration)
    {
        pairingExpiresAt = DateTimeOffset.UtcNow + duration;
    }

    public void DisablePairing()
    {
        pairingExpiresAt = null;
    }
}
