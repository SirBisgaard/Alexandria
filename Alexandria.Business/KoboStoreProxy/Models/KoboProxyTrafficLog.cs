namespace Alexandria.Business.KoboStoreProxy.Models;

public class KoboProxyTrafficLog
{
    // DB
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    // Request
    public required string Method { get; set; }
    public required string Url { get; set; }
    public required string RequestHeaders { get; set; }
    public required string RequestBody { get; set; }

    // Response
    public int StatusCode { get; set; }
    public required string ResponseHeaders { get; set; }
    public required string ResponseBody { get; set; }

    // Debug
    public long DurationMs { get; set; }
}