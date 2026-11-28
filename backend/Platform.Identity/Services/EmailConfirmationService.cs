using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

public sealed class EmailConfirmationService(
    PlatformIdentityDbContext db,
    AccountDbContext accountDb,
    TimeProvider clock) : IEmailConfirmationService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public async Task<EmailConfirmationGrant?> IssueAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = CredentialVerifier.NormalizeEmail(email);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Email == normalizedEmail, ct);
        if (account is null || account.EmailConfirmed)
            return null;

        var now = clock.GetUtcNow();
        var rawToken = CreateToken();
        var token = new EmailConfirmationToken(
            Guid.NewGuid(),
            account.Id,
            HashToken(rawToken),
            now,
            now.Add(TokenLifetime));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await accountDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

        await db.EmailConfirmationTokens
            .Where(t => t.UserId == account.Id && t.ConsumedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, now), ct);

        db.EmailConfirmationTokens.Add(token);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new EmailConfirmationGrant(account.Email, rawToken);
    }

    public async Task<bool> ConfirmAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return false;

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await accountDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

        var tokenRecord = await db.EmailConfirmationTokens
            .SingleOrDefaultAsync(t => t.TokenHash == HashToken(token), ct);
        if (tokenRecord is null || tokenRecord.ExpiresAt <= now
            || tokenRecord.ConsumedAt is not null || tokenRecord.RevokedAt is not null)
            return false;

        var consumed = await db.EmailConfirmationTokens
            .Where(t => t.Id == tokenRecord.Id
                && t.ConsumedAt == null
                && t.RevokedAt == null
                && t.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.ConsumedAt, now), ct);
        if (consumed != 1)
            return false;

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == tokenRecord.UserId, ct);
        var identityUser = await accountDb.Users.SingleOrDefaultAsync(u => u.Id == tokenRecord.UserId, ct);
        if (account is null || identityUser is null || account.EmailConfirmed || identityUser.EmailConfirmed)
            return false;

        account.ConfirmEmail();
        identityUser.EmailConfirmed = true;
        await db.SaveChangesAsync(ct);
        await accountDb.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return true;
    }

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
