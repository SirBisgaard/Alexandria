using Alexandria.Business.Models.Proxy;

namespace Alexandria.Business.Interfaces;

public interface ISyncTokenService
{
    KoboDeviceSyncToken Parse(string? headerValue);
    string Generate(KoboDeviceSyncToken token);
}
