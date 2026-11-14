using Platform.Identity.Entities;

namespace Platform.Identity.Interfaces.Mfa;

public interface IMfaCredentialsStore
{
    Task<MfaCredentials?> GetAsync(Guid userId, CancellationToken ct);
    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    Task<bool> CreatePendingAsync(
        Guid userId,
        string encryptedSecret,
        int keyVersion,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    Task<bool> ConfirmAsync(Guid userId, long timeStepMatched, CancellationToken ct);
}