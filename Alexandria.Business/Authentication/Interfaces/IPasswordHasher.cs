namespace Alexandria.Business.Authentication.Interfaces;

public interface IPasswordHasher
{
    /// <summary>
    /// Derives a PBKDF2-SHA256 hash of the given password with a fresh random salt and returns
    /// a self-describing PHC-style string that carries the algorithm, iteration count, salt and hash.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Constant-time verification of a password against a previously stored PHC string.
    /// Returns false for malformed stored values rather than throwing.
    /// </summary>
    bool Verify(string password, string stored);
}
