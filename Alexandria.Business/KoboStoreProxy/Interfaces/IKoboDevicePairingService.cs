namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDevicePairingService
{
    bool IsPairingEnabled();
    DateTimeOffset? GetPairingExpiry();
    void EnablePairing(TimeSpan duration);
    void DisablePairing();
}
