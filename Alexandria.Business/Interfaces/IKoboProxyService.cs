using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IKoboProxyService
{
    Task<KoboProxyResponse> Proxy(KoboProxyRequest request);
    Task<KoboProxyResponse> Initialization(KoboProxyRequest request);
    Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request);
    Task<KoboProxyResponse> Download(KoboProxyRequest request, string uuid);
    Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, string uuid, int width, int height, string? quality = null, string? greyscale = null);
    Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, string uuid);
    Task<KoboProxyResponse> Metadata(KoboProxyRequest request, string uuid);
    Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, string uuid);
}