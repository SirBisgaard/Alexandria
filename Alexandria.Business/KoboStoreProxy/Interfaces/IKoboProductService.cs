using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboProductService
{
    Task<KoboProxyResponse> Series(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> Recommendations(KoboProxyRequest request);
    Task<KoboProxyResponse> NextRead(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> ProductRecommendations(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> AudiobookProduct(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> EbookProduct(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> ProductPrices(KoboProxyRequest request, string uuids);
}
