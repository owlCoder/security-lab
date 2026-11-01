using Microsoft.EntityFrameworkCore;
using Platform.Identity.Data;
using Platform.Identity.Domain;

namespace Platform.Identity.Services;

public sealed record CredentialRecord(Guid AccountId, AccountStatus Status, string? PasswordHash);

/// <summary>Finds the data needed for a login check. The e-mail passed in is already normalized.</summary>
public interface ICredentialStore
{
    Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
}

/// <param name="IsValid">True only when the account exists and the password matches.</param>
public sealed record CredentialResult(bool IsValid, Guid AccountId = default, AccountStatus Status = default)
{
    public static CredentialResult Invalid { get; } = new(false);
}

public interface ICredentialVerifier
{
    /// <summary>Checks e-mail and password. It does NOT look at the account status; the caller decides.</summary>
    Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default);
}

public sealed class CredentialVerifier(ICredentialStore store, IPasswordHasher hasher) : ICredentialVerifier
{
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 128;

    /// <summary>Accounts must be stored with this form of the e-mail (also when seeded).</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        var record = await store.FindByEmailAsync(NormalizeEmail(email), ct);

        // Verify runs even when the account is unknown, so timing does not reveal which e-mails exist.
        var passwordMatches = hasher.Verify(password, record?.PasswordHash);

        return record is not null && passwordMatches
            ? new CredentialResult(true, record.AccountId, record.Status)
            : CredentialResult.Invalid;
    }
}

internal sealed class EfCredentialStore(PlatformIdentityDbContext db) : ICredentialStore
{
    public Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.Accounts
            .AsNoTracking()
            .Where(account => account.Email == normalizedEmail)
            .Select(account => new CredentialRecord(account.Id, account.Status, account.PasswordHash))
            .FirstOrDefaultAsync(ct);
}
