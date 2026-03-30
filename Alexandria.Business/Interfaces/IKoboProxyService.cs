using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IKoboProxyService
{
    Task<KoboProxyResponse> Proxy(KoboProxyRequest request);
}