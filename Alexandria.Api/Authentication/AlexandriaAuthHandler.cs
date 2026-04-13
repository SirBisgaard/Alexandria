using System.Security.Claims;
using System.Text.Encodings.Web;
using Alexandria.Business.Auth;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Alexandria.Api.Authentication;

/// <summary>
/// Authenticates frontend requests via a <c>?t=</c> query-string HMAC token minted at login.
/// Token format is identical to the Kobo device scheme — <c>base64url(userUuid).base64url(hmac)</c> —
/// just keyed off the user HMAC key. Verified tokens resolve to the user record; a missing record
/// (deleted user) fails auth, which is the intended revocation path.
/// </summary>
public class AlexandriaAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, IUserRepository userRepository, AuthSecretProvider authSecrets) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "Alexandria";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The Alexandria scheme is the default, so it runs on every request — bail out immediately
        // for static files and the Kobo proxy surface so we don't pointlessly crack tokens for them.
        if (!Request.Path.StartsWithSegments("/api/alexandria"))
            return AuthenticateResult.NoResult();

        var token = ExtractToken();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        if (!TokenHelpers.TryVerify(token, authSecrets.GetUserHmacKey(), out var userUuidString))
            return AuthenticateResult.Fail("Invalid or expired token");

        if (!Guid.TryParse(userUuidString, out var userUuid))
            return AuthenticateResult.Fail("Malformed user id in token");

        var user = await userRepository.GetByUuid(userUuid);
        if (user is null)
            return AuthenticateResult.Fail("User not found");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserUuid.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("username", user.Username)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>
    /// Reads the token from <c>Authorization: Bearer ...</c> first (used by JSON API callers) and
    /// falls back to the <c>?t=</c> query string (used by <c>&lt;img&gt;</c> cover tags, which
    /// can't attach custom headers).
    /// </summary>
    private string? ExtractToken()
    {
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        return Request.Query["t"].FirstOrDefault();
    }
}
