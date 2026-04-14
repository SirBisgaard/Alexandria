using System.Security.Cryptography;
using System.Text;

namespace Alexandria.Business.Auth;

/// <summary>
/// Derives HMAC keys for user tokens from the application's UUID seed.
/// Initialized during boot after the seed is loaded from the database.
/// </summary>
public class AuthSecretProvider
{
    private byte[]? userHmacKey;

    public void Initialize(Guid uuidSeed)
    {
        var seedBytes = Encoding.UTF8.GetBytes(uuidSeed.ToString());
        userHmacKey = HMACSHA256.HashData(seedBytes, "alexandria-user"u8.ToArray());
    }

    public byte[] GetUserHmacKey()
    {
        return userHmacKey ?? throw new InvalidOperationException("Auth secrets not initialized");
    }
}
