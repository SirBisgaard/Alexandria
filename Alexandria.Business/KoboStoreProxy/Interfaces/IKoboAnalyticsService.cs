using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboAnalyticsService
{
    Task<KoboProxyResponse> AnalyticsEvent(KoboProxyRequest request);
}
