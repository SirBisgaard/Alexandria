using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Alexandria.DataAccess.Database.Interfaces;
using Alexandria.DataAccess.Database.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Alexandria.Api.Authentication;

/// <summary>
/// Authenticates Kobo device requests using the Device Key from the route.
/// The Device Key is a GUID pre-registered by the user in Alexandria's UI.
/// On first contact, the device's serial number (from <c>x-kobo-deviceid</c> header) is bound
/// to the key. If a different serial is seen later, the device is locked and returns 401.
/// </summary>
public partial class KoboDeviceAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, IKoboDeviceRepository deviceRepository) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "KoboDevice";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var deviceKeyString = Request.RouteValues["devicekey"]?.ToString();
        if (string.IsNullOrEmpty(deviceKeyString) || !Guid.TryParse(deviceKeyString, out var deviceUuid))
            return AuthenticateResult.Fail("Invalid device key");

        var device = await deviceRepository.GetByUuid(deviceUuid);
        if (device is null)
            return AuthenticateResult.Fail("Device not registered");

        if (device.IsLocked)
            return AuthenticateResult.Fail("Device is locked");

        var serialNumber = Request.Headers["x-kobo-deviceid"].FirstOrDefault();
        if (!string.IsNullOrEmpty(serialNumber))
        {
            if (device.SerialNumber is null)
            {
                var userAgent = Request.Headers.UserAgent.ToString();
                var match = KoboUserAgentRegex().Match(userAgent);
                string? modelCode = null, firmware = null, modelName = null;
                if (match.Success)
                {
                    modelCode = match.Groups[1].Value;
                    firmware = match.Groups[2].Value;
                    modelName = KoboDeviceRepository.GetModelName(modelCode);
                }
                await deviceRepository.BindDevice(deviceUuid, serialNumber, modelName, modelCode, firmware);
            }
            else if (!string.Equals(device.SerialNumber, serialNumber, StringComparison.Ordinal))
            {
                await deviceRepository.SetLocked(deviceUuid, true);
                return AuthenticateResult.Fail("Serial number mismatch — device locked");
            }
        }

        await deviceRepository.UpdateLastRequest(deviceUuid);
        Context.Items["Device"] = device;
        return AuthenticateResult.Success(CreateTicket(deviceUuid.ToString()));
    }

    private static AuthenticationTicket CreateTicket(string deviceUuid)
    {
        var claims = new[] { new Claim("device_uuid", deviceUuid) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }

    [GeneratedRegex(@"\(Kobo Touch (\d{4})/([\d.]+)\)")]
    private static partial Regex KoboUserAgentRegex();
}
