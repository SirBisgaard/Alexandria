using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class KoboProxyService(HttpClient httpClient, IKoboProxyLogger koboProxyLogger) : IKoboProxyService
{
    public async Task<KoboProxyResponse> Proxy(KoboProxyRequest request)
    {
        return await Task.FromResult(new KoboProxyResponse
        {
            StatusCode = 200,
            Headers = request.Headers,
            Body = []
        });
    }
}