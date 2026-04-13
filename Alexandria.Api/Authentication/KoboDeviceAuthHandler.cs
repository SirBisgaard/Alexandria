using System.Security.Claims;
using System.Text.Encodings.Web;
using Alexandria.Business.Auth;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Alexandria.Api.Authentication;

/// <summary>
/// Authenticates Kobo device requests. Two signals are accepted:
/// <c>x-kobo-deviceid</c> header (trusted as-is, sent by the device's JSON API client) or
/// <c>?t=</c> query string carrying an HMAC token (used by the device's image / download / audiobook
/// fetchers, which don't attach custom headers). When pairing mode is enabled we additionally
/// upsert any device we see with a header so it becomes a registered device for future requests.
/// </summary>
public class KoboDeviceAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, IKoboDeviceRepository deviceRepository, IKoboDevicePairingService pairingService, AuthSecretProvider authSecrets) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "KoboDevice";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var pairing = pairingService.IsPairingEnabled();
        var headerDeviceId = Request.Headers["x-kobo-deviceid"].FirstOrDefault();

        // Pair mode: whatever device shows up with an x-kobo-deviceid header gets auto-registered
        // if we haven't seen it before. Requests without the header (covers, downloads) pass through
        // anonymously so first-contact content fetches don't 401 during the pairing window.
        if (pairing)
        {
            if (!string.IsNullOrEmpty(headerDeviceId))
            {
                var existing = await deviceRepository.GetByDeviceId(headerDeviceId);
                if (existing is null)
                    await deviceRepository.Upsert(new KoboDevice { DeviceUuid = Guid.NewGuid(), DeviceId = headerDeviceId });
                await deviceRepository.UpdateLastRequest(headerDeviceId);
                return AuthenticateResult.Success(CreateTicket(headerDeviceId));
            }
            return AuthenticateResult.Success(CreateTicket("pair-mode"));
        }

        var deviceId = headerDeviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            var token = ExtractPathToken() ?? Request.Query["t"].FirstOrDefault();
            if (TokenHelpers.TryVerify(token, authSecrets.GetDeviceHmacKey(), out var tokenDeviceId))
                deviceId = tokenDeviceId;
        }

        if (string.IsNullOrEmpty(deviceId))
            return AuthenticateResult.Fail("No device identification");

        var device = await deviceRepository.GetByDeviceId(deviceId);
        if (device is null)
            return AuthenticateResult.Fail("Device not registered");

        await deviceRepository.UpdateLastRequest(deviceId);
        return AuthenticateResult.Success(CreateTicket(deviceId));
    }

    /// <summary>
    /// Pulls the token out of the <c>/t/{token}/...</c> route segment. Uses <c>RouteValues</c>
    /// when routing has run (content-route actions) and falls back to a path-string parse for
    /// requests that haven't hit routing yet (rare, but defensive).
    /// </summary>
    private string? ExtractPathToken()
    {
        if (Request.RouteValues.TryGetValue("token", out var routeToken) && routeToken is string fromRoute && !string.IsNullOrEmpty(fromRoute))
            return fromRoute;

        var path = Request.Path.Value;
        if (string.IsNullOrEmpty(path))
            return null;
        const string prefix = "/api/kobo/t/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var afterPrefix = path[prefix.Length..];
        var nextSlash = afterPrefix.IndexOf('/');
        return nextSlash < 0 ? afterPrefix : afterPrefix[..nextSlash];
    }

    private static AuthenticationTicket CreateTicket(string deviceId)
    {
        var claims = new[] { new Claim("device_id", deviceId) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }
}
