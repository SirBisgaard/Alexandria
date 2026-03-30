using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public interface IKoboProxyService
{
    Task<KoboProxyResponse> Proxy(KoboProxyRequest request);
}