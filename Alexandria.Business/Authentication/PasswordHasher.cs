using System.Security.Cryptography;
using System.Text;
using Alexandria.Business.Authentication.Interfaces;

namespace Alexandria.Business.Authentication;

/// <summary>
/// PBKDF2-SHA256 password hasher. Produces and consumes PHC-style strings of the form
/// <c>$pbkdf2-sha256$i={iterations}$&lt;base64-salt&gt;$&lt;base64-hash&gt;</c>.
/// The iteration count is recorded per-hash, so raising <see cref="DefaultIterations"/> later
/// does not invalidate existing hashes — they continue to verify at their recorded cost.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int DefaultIterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return $"${Algorithm}$i={DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return false;

        var parts = stored.Split('$');
        // Expected layout: ["", "pbkdf2-sha256", "i=600000", "<b64-salt>", "<b64-hash>"]
        if (parts.Length != 5 || parts[0].Length != 0 || parts[1] != Algorithm)
            return false;

        if (!parts[2].StartsWith("i=") || !int.TryParse(parts[2].AsSpan(2), out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
