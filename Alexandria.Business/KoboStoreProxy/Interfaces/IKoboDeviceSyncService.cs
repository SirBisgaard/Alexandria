using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboDeviceSyncService
{
    Task<KoboProxyResponse> Initialization(KoboProxyRequest request);
    Task<KoboProxyResponse> LibrarySync(KoboProxyRequest request);
    Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null);
    Task<KoboProxyResponse> ReadingState(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> Metadata(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> DownloadManifest(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> AudiobookChapter(KoboProxyRequest request, Guid uuid, int partId);
    Task<KoboProxyResponse> DeleteBook(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> Series(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> Recommendations(KoboProxyRequest request);
    Task<KoboProxyResponse> NextRead(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> ProductRecommendations(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> AudiobookProduct(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> EbookProduct(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> ProductPrices(KoboProxyRequest request, string uuids);
}
