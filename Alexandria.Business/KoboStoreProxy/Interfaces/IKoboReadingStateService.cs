using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboReadingStateService
{
    Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid);
}
