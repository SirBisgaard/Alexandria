using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.KoboStoreProxy.Interfaces;

public interface IKoboContentDeliveryService
{
    Task<KoboProxyResponse> Download(KoboProxyRequest request, Guid uuid);
    Task<KoboProxyResponse> CoverImage(KoboProxyRequest request, Guid uuid, int width, int height, string? quality = null, string? greyscale = null);
    Task<KoboProxyResponse> DownloadManifest(KoboProxyRequest request, Guid uuid);
    Task<Book?> ResolveLocalAudiobook(Guid uuid);
    Task StreamAudiobookChapter(Book book, int partId, Stream outputStream, CancellationToken cancellationToken);
}
