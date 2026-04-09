using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboProxyService
{
    Task<KoboProxyResponse> Proxy(KoboProxyRequest request);
}
