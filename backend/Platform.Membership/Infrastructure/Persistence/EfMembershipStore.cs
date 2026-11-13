
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Infrastructure.Persistence;

/// <summary>
/// Svaka operacija otvara svoj kratki DbContext preko fabrike, pa se ova klasa
/// bezbedno koristi i iz singleton politika pristupa (bez keša između zahteva).
/// </summary>
internal sealed class EfMembershipStore(IDbContextFactory<MembershipDbContext> contexts) : IMembershipStore
{
    public async ValueTask<CourseMember?> FindActiveAsync(
        Guid courseId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        return await db.CourseMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.CourseId == courseId
                     && m.AccountId == accountId
                     && m.Status == MemberStatus.Active,
                cancellationToken);
    }

    public async ValueTask<AddMemberResult> AddAsync(
        CourseMember member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.CourseMembers.Add(member);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AddMemberResult.Added;
        }
        catch (DbUpdateException exception) when (IsActiveMemberConflict(exception))
        {
            // Drugi zahtev je upisao istog korisnika u međuvremenu; baza je odbila duplikat.
            return AddMemberResult.AlreadyActive;
        }
    }

    private static bool IsActiveMemberConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres
        && string.Equals(
            postgres.ConstraintName,
            CourseMemberConfiguration.ActiveMemberIndexName,
            StringComparison.Ordinal);
}
