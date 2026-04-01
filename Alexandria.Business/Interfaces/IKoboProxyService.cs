using Alexandria.Business.Models.Proxy;

namespace Alexandria.Business.Interfaces;

public interface IKoboProxyService
{
    Task<KoboProxyResponse> Proxy(KoboProxyRequest request);
    Task<KoboProxyResponse> Initialization(KoboProxyRequest request);
    Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request);
    Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null);
    Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid);
}