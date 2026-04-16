namespace Alexandria.Business.Library.Models;

/// <summary>
/// An Alexandria user account. Passwords are stored as bcrypt hashes.
/// </summary>
public class User
{
    public Guid UserUuid { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Bcrypt password hash. Never stored or transmitted in plaintext.</summary>
    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public DateTimeOffset LastLoginAt { get; set; }
}
