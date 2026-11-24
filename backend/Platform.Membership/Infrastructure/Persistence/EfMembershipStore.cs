using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

/// <summary>
/// Svaka operacija otvara kratak DbContext preko fabrike (bezbedno iz singleton politika).
/// Upis i audit zapis su jedna PostgreSQL transakcija (pravilo S11-06 Tima 12).
/// </summary>
internal sealed class EfMembershipStore(
    IDbContextFactory<MembershipDbContext> contexts,
    ITransactionalAuditWriter audit) : IMembershipStore
{
    public async ValueTask<CourseMember?> FindActiveAsync(
        Guid courseId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.CourseMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.CourseId == courseId && m.AccountId == accountId && m.Status == MemberStatus.Active,
                cancellationToken);
    }

    public async ValueTask<AddMemberResult> AddAsync(
        CourseMember member, AuditContext auditContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(auditContext);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.CourseMembers.Add(member);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveMemberConflict(exception))
        {
            // Drugi zahtev je upisao istog korisnika; izlazak bez commit-a poništava transakciju.
            return AddMemberResult.AlreadyActive;
        }

        var recorded = await audit.WriteAsync(
            MembershipAuditEvents.Enrolled(member),
            auditContext,
            transaction.GetDbTransaction(),
            cancellationToken);

        // Nema audit zapisa -> nema upisa.
        if (!recorded.IsRecorded)
        {
            return AddMemberResult.AuditUnavailable;
        }

        await transaction.CommitAsync(cancellationToken);
        return AddMemberResult.Added;
    }

    private static bool IsActiveMemberConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && string.Equals(postgres.ConstraintName, CourseMemberConfiguration.ActiveMemberIndexName, StringComparison.Ordinal);
}