using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

public sealed class EfPasswordResetTokenStore(
    PlatformIdentityDbContext db)
    : IPasswordResetTokenStore
{
    private const int MaxHashLength = 256;

    public async Task<bool> TrySaveAsync(
        PasswordResetToken token,
        CancellationToken cancellationToken = default)
    {
        if (!token.IsValid() ||
            token.IsUsed ||
            token.TokenHash.Length > MaxHashLength ||
            token.CreatedAt.Offset != TimeSpan.Zero ||
            token.ExpiresAt.Offset != TimeSpan.Zero)
        {
            return false;
        }

        db.PasswordResetTokens.Add(token);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName ==
                "ux_password_reset_tokens_token_hash")
        {
            db.Entry(token).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<PasswordResetTokenLookupResult> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (tokenHash.AsSpan().Trim().IsEmpty ||
            tokenHash.Length > MaxHashLength)
        {
            return new PasswordResetTokenLookupResult.NotFound();
        }

        var records = await db.PasswordResetTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new PasswordResetTokenSnapshot(
                token.Id,
                token.UserId,
                token.TokenHash,
                token.CreatedAt,
                token.ExpiresAt,
                token.IsUsed,
                token.UsedAt))
            .Take(1)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return new PasswordResetTokenLookupResult.NotFound();
        }

        return new PasswordResetTokenLookupResult.Found(
            records[0]);
    }

    public async Task<bool> TryConsumeAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (tokenHash.AsSpan().Trim().IsEmpty ||
            tokenHash.Length > MaxHashLength)
        {
            return false;
        }

        var utcNow = now.ToUniversalTime();

        int updatedRows = await db.PasswordResetTokens
            .Where(token =>
                token.TokenHash == tokenHash &&
                !token.IsUsed &&
                token.CreatedAt <= utcNow &&
                token.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsUsed, true)
                .SetProperty(token => token.UsedAt, utcNow),
                cancellationToken);

        return updatedRows == 1;
    }
}
