
using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

/// <summary>
/// ACC-07-2: delegacije u PostgreSQL-u. Svaka operacija otvara kratak DbContext (bezbedno
/// kao singleton). Dodela, opoziv i zapis o isteku su jedna transakcija sa audit zapisom
/// (pravilo S11-06 Tima 12). Opoziv i zapis o isteku su uslovni UPDATE, pa dva istovremena
/// zahteva ne mogu oba da uspeju.
/// </summary>
internal sealed class EfDelegationStore(
    IDbContextFactory<MembershipDbContext> contexts,
    ITransactionalAuditWriter audit) : IDelegationStore
{
    public async ValueTask<CourseDelegation?> FindAsync(
        Guid courseId, Guid delegationId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.CourseDelegations
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == delegationId && d.CourseId == courseId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<CourseDelegation>> ListForCourseAsync(
        Guid courseId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.CourseDelegations
            .AsNoTracking()
            .Where(d => d.CourseId == courseId)
            .OrderByDescending(d => d.GrantedAt)
            .ToListAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlySet<string>> ActiveActionsAsync(
        Guid courseId, Guid granteeId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var utcNow = now.ToUniversalTime();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var actions = await db.CourseDelegations
            .AsNoTracking()
            .Where(d => d.CourseId == courseId
                        && d.GranteeId == granteeId
                        && d.RevokedAt == null
                        && d.ExpiresAt > utcNow)
            .Select(d => d.Action)
            .Distinct()
            .ToListAsync(cancellationToken);

        return actions.ToFrozenSet(StringComparer.Ordinal);
    }

    public async ValueTask<bool> AddAsync(
        CourseDelegation delegation, AuditContext auditContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delegation);
        ArgumentNullException.ThrowIfNull(auditContext);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.CourseDelegations.Add(delegation);
        await db.SaveChangesAsync(cancellationToken);

        var recorded = await audit.WriteAsync(
            DelegationAuditEvents.Granted(delegation),
            auditContext,
            transaction.GetDbTransaction(),
            cancellationToken);

        // Nema audit zapisa -> nema dodele (izlazak bez commit-a poništava transakciju).
        if (!recorded.IsRecorded)
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<RevokeDelegationResult> RevokeAsync(
        Guid courseId,
        Guid delegationId,
        Guid revokedBy,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var utcNow = now.ToUniversalTime();

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var delegation = await db.CourseDelegations
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == delegationId && d.CourseId == courseId, cancellationToken);
        if (delegation is null)
        {
            return RevokeDelegationResult.NotFound;
        }

        if (!delegation.IsActiveAt(utcNow))
        {
            return RevokeDelegationResult.NotActive;
        }

        // Pravila domena (ko i kada); vrednosti se upisuju uslovnim UPDATE-om ispod.
        delegation.Revoke(revokedBy, utcNow);

        var changed = await db.CourseDelegations
            .Where(d => d.Id == delegationId
                        && d.CourseId == courseId
                        && d.RevokedAt == null
                        && d.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.RevokedAt, delegation.RevokedAt)
                    .SetProperty(d => d.RevokedBy, delegation.RevokedBy),
                cancellationToken);
        if (changed != 1)
        {
            // Drugi zahtev je u međuvremenu opozvao, ili je delegacija upravo istekla.
            return RevokeDelegationResult.NotActive;
        }

        var recorded = await audit.WriteAsync(
            DelegationAuditEvents.Revoked(delegation),
            auditContext,
            transaction.GetDbTransaction(),
            cancellationToken);
        if (!recorded.IsRecorded)
        {
            return RevokeDelegationResult.AuditUnavailable;
        }

        await transaction.CommitAsync(cancellationToken);
        return RevokeDelegationResult.Revoked;
    }

    public async ValueTask<IReadOnlyList<CourseDelegation>> ExpiredWithoutRecordAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
    {
        var utcNow = now.ToUniversalTime();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.CourseDelegations
            .AsNoTracking()
            .Where(d => d.RevokedAt == null && d.ExpiryRecordedAt == null && d.ExpiresAt <= utcNow)
            .OrderBy(d => d.ExpiresAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async ValueTask<bool> RecordExpiryAsync(
        Guid delegationId,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        var utcNow = now.ToUniversalTime();

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var delegation = await db.CourseDelegations
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == delegationId, cancellationToken);
        if (delegation is null || delegation.StateAt(utcNow) != DelegationState.Expired)
        {
            return false;
        }

        // Uslovni UPDATE: istek se upisuje najviše jednom, i kad dva posla rade istovremeno.
        var changed = await db.CourseDelegations
            .Where(d => d.Id == delegationId
                        && d.RevokedAt == null
                        && d.ExpiryRecordedAt == null
                        && d.ExpiresAt <= utcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(d => d.ExpiryRecordedAt, (DateTimeOffset?)utcNow),
                cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        var recorded = await audit.WriteAsync(
            DelegationAuditEvents.Expired(delegation),
            auditContext,
            transaction.GetDbTransaction(),
            cancellationToken);
        if (!recorded.IsRecorded)
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
