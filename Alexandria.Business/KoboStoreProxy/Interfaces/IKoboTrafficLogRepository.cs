using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboTrafficLogRepository
{
    Task Log(KoboProxyTrafficLog entry);
}