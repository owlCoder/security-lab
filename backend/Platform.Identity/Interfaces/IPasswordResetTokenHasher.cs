namespace Platform.Identity.Interfaces;

public interface IPasswordResetTokenHasher
{
    bool TryComputeHash(
        string token,
        out string tokenHash);
}
