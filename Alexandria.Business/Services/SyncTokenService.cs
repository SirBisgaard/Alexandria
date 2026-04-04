using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Proxy;

namespace Alexandria.Business.Services;

public class SyncTokenService : ISyncTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public KoboDeviceSyncToken Parse(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return new KoboDeviceSyncToken();
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
            var token = JsonSerializer.Deserialize<KoboDeviceSyncToken>(json, JsonOptions);
            if (token is not null && token.V >= 1)
            {
                return token;
            }
        }
        catch
        {
            // Not our token format — treat as raw Kobo store token (first sync or factory reset)
        }

        return new KoboDeviceSyncToken { KoboToken = headerValue };
    }

    public string Generate(KoboDeviceSyncToken token)
    {
        var json = JsonSerializer.Serialize(token, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
