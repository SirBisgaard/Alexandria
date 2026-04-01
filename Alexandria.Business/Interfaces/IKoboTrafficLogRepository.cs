using Alexandria.Business.Models.Proxy;

namespace Alexandria.Business.Interfaces;

public interface IKoboTrafficLogRepository
{
    Task Log(KoboProxyTrafficLog entry);
}