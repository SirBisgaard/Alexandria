namespace Alexandria.Business.Services;

public interface IKoboProxyLogger
{
    void FromKoboDevice(string method, string uri, string headers, string body);
}

public class KoboProxyLogger : IKoboProxyLogger
{
    
}