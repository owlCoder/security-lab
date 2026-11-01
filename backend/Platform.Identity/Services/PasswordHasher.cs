using BCrypt.Net;

namespace Platform.Identity.Services;

/// <summary>Password hashing for the Identity module. Single place that knows the algorithm (BCrypt).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored hash. When <paramref name="passwordHash"/> is null or malformed
    /// the same amount of work is still done and the result is false, so unknown e-mails and wrong
    /// passwords cost the same time.
    /// </summary>
    bool Verify(string password, string? passwordHash);
}

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    // Valid hash of a random password nobody knows. Only used to burn the same time as a real check.
    private static readonly Lazy<string> DummyHash =
        new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString("N"), WorkFactor));

    // Enhanced = SHA-384 pre-hash, so passwords longer than 72 bytes are not silently truncated.
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);

    public bool Verify(string password, string? passwordHash)
    {
        try
        {
            var matches = BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash ?? DummyHash.Value);
            return matches && passwordHash is not null;
        }
        catch (Exception e) when (e is SaltParseException or ArgumentException)
        {
            return false;
        }
    }
}
