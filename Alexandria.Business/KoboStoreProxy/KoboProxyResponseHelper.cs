using System.Text;
using System.Text.Json;
using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy;

public static class KoboProxyResponseHelper
{
    public static KoboProxyResponse JsonResponse(int statusCode, object body, Dictionary<string, string[]>? extraHeaders = null)
    {
        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        var headers = extraHeaders ?? new Dictionary<string, string[]>();
        headers["Content-Type"] = ["application/json; charset=utf-8"];
        return new KoboProxyResponse { StatusCode = statusCode, Headers = headers, Body = bytes };
    }
}
