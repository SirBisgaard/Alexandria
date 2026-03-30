namespace Alexandria.Business.Interfaces;

public interface IKoboTrafficLogRepository
{
    Task Log(string method, string url, string requestHeaders, string requestBody, int statusCode, string responseHeaders, string responseBody, long durationMs);
}