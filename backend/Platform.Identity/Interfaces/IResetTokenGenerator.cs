namespace Platform.Identity.Interfaces;

public interface IResetTokenGenerator
{
    string GenerateToken();

    bool TryComputeHash(string token, out string tokenHash);

    bool VerifyHash(string token, string storedHash);
}
