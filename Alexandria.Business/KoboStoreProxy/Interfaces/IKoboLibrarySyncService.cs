using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboLibrarySyncService
{
    Task<KoboProxyResponse> Initialization(KoboProxyRequest request);
    Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request);
    Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid);
}
