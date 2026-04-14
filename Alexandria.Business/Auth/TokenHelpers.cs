using System.Security.Cryptography;
using System.Text;

namespace Alexandria.Business.Auth;

/// <summary>
/// Mints and verifies HMAC-signed tokens used for both user and device auth.
/// Format: <c>base64url(id|expiryUnixSeconds) + "." + base64url(HMAC-SHA256(key, id|expiryUnixSeconds))</c>.
/// Expiry is signed into the payload — tampering with either <c>id</c> or the unix-seconds
/// expiry invalidates the signature. Tokens are self-contained (no server-side session table).
/// </summary>
public static class TokenHelpers
{
    /// <summary>How long a user token minted by <c>AuthController.Login</c> stays valid.</summary>
    public static readonly TimeSpan UserTokenTtl = TimeSpan.FromDays(7);

    public static string Mint(string id, byte[] key, TimeSpan ttl)
    {
        var expiry = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{id}|{expiry}");
        var sig = HMACSHA256.HashData(key, payload);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(sig)}";
    }

    public static bool TryVerify(string? token, byte[] key, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(token))
            return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return false;

        var payloadBytes = Base64UrlDecode(token[..dot]);
        var sigBytes = Base64UrlDecode(token[(dot + 1)..]);
        if (payloadBytes is null || sigBytes is null)
            return false;

        var expected = HMACSHA256.HashData(key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, sigBytes))
            return false;

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var pipe = payload.LastIndexOf('|');
        if (pipe <= 0 || pipe == payload.Length - 1)
            return false;

        if (!long.TryParse(payload[(pipe + 1)..], out var expiryUnix))
            return false;

        if (DateTimeOffset.FromUnixTimeSeconds(expiryUnix) < DateTimeOffset.UtcNow)
            return false;

        id = payload[..pipe];
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[]? Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
