using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboContentDeliveryService
{
    Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null);
    Task<KoboProxyResponse> AudiobookChapter(KoboProxyRequest request, Guid uuid, int partId);
    Task<KoboProxyResponse> DownloadManifest(KoboProxyRequest request, Guid uuid);
}
