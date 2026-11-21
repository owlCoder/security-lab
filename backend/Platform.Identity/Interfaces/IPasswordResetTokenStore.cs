using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IPasswordResetTokenStore
{
    Task<bool> TrySaveAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default);

    Task<PasswordResetTokenLookupResult> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task<bool> TryConsumeAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed record PasswordResetTokenSnapshot(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsUsed,
    DateTimeOffset UsedAt);

public abstract record PasswordResetTokenLookupResult
{
    public sealed record Found(PasswordResetTokenSnapshot Token)
        : PasswordResetTokenLookupResult;

    public sealed record NotFound
        : PasswordResetTokenLookupResult;
}
