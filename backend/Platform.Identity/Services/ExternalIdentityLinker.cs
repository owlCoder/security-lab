using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

internal sealed class ExternalIdentityLinker(
    PlatformIdentityDbContext db,
    TimeProvider time) : IExternalIdentityLinker
{
    internal const string IssuerSubjectIndex = "ux_external_logins_issuer_subject";
    internal const string AccountIssuerIndex = "ux_external_logins_account_issuer";

    public async Task<FederatedLoginResult> ResolveAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!IsValidKey(identity.Issuer, ExternalLogin.MaxIssuerLength) ||
            !IsValidKey(identity.Subject, ExternalLogin.MaxSubjectLength))
            return FederatedLoginResult.Refused(FederatedLoginOutcome.InvalidIdentity);

        var now = time.GetUtcNow();

        // 1. Repeated identity: the link decides, never the e-mail.
        var existing = await FindLinkAsync(identity.Issuer, identity.Subject, ct);
        if (existing is not null)
        {
            existing.RecordLogin(now);
            await db.SaveChangesAsync(ct);
            return FederatedLoginResult.Existing(existing.AccountId);
        }

        // 2. First login: an unverified e-mail must not give access to someone else's account.
        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
            return FederatedLoginResult.Refused(FederatedLoginOutcome.EmailNotVerified);

        var email = CredentialVerifier.NormalizeEmail(identity.Email);
        if (email.Length > CredentialVerifier.MaxEmailLength)
            return FederatedLoginResult.Refused(FederatedLoginOutcome.NoMatchingAccount);

        var accountId = await db.Accounts
            .AsNoTracking()
            .Where(account => account.Email == email)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(ct);

        if (accountId is null)
            return FederatedLoginResult.Refused(FederatedLoginOutcome.NoMatchingAccount);

        var linkedSubject = await db.ExternalLogins
            .AsNoTracking()
            .Where(login => login.AccountId == accountId && login.Issuer == identity.Issuer)
            .Select(login => login.Subject)
            .SingleOrDefaultAsync(ct);
        if (linkedSubject is not null)
        {
            // A concurrent first login of this same identity may have linked it after the lookup above.
            return linkedSubject == identity.Subject
                ? FederatedLoginResult.Existing(accountId.Value)
                : FederatedLoginResult.Refused(FederatedLoginOutcome.AccountAlreadyLinked);
        }

        // 3. Insert; the database has the final word on duplicates.
        var link = new ExternalLogin(Guid.NewGuid(), accountId.Value, identity.Issuer, identity.Subject, now);
        db.ExternalLogins.Add(link);

        try
        {
            await db.SaveChangesAsync(ct);
            return FederatedLoginResult.New(accountId.Value);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();

            // Either unique index: if this same identity was linked by a concurrent first login,
            // return its account; otherwise another identity of this issuer owns the account.
            var winner = await FindLinkAsync(identity.Issuer, identity.Subject, ct);
            return winner is not null
                ? FederatedLoginResult.Existing(winner.AccountId)
                : FederatedLoginResult.Refused(FederatedLoginOutcome.AccountAlreadyLinked);
        }
    }

    private Task<ExternalLogin?> FindLinkAsync(string issuer, string subject, CancellationToken ct) =>
        db.ExternalLogins.SingleOrDefaultAsync(login => login.Issuer == issuer && login.Subject == subject, ct);

    private static bool IsValidKey(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}