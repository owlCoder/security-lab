
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Tests.Testing;

/// <summary>
/// Test dvojnik skladišta. Oponaša pravilo iz baze: najviše jedno aktivno članstvo
/// po (kurs, korisnik). Pravo pravilo u PostgreSQL-u proverava MembershipPostgresTests.
/// </summary>
internal sealed class InMemoryMembershipStore : IMembershipStore
{
    private readonly object gate = new();
    private readonly List<CourseMember> members = [];

    /// <summary>Simulira da je drugi zahtev upisao istog korisnika između provere i upisa.</summary>
    public bool SimulateConcurrentInsert { get; set; }

    public IReadOnlyList<CourseMember> All
    {
        get
        {
            lock (gate)
            {
                return members.ToList();
            }
        }
    }

    public ValueTask<CourseMember?> FindActiveAsync(
        Guid courseId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return ValueTask.FromResult(members.SingleOrDefault(m =>
                m.CourseId == courseId && m.AccountId == accountId && m.Status == MemberStatus.Active));
        }
    }

    public ValueTask<AddMemberResult> AddAsync(
        CourseMember member,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (SimulateConcurrentInsert)
            {
                return ValueTask.FromResult(AddMemberResult.AlreadyActive);
            }

            var duplicate = member.Status == MemberStatus.Active && members.Any(m =>
                m.CourseId == member.CourseId
                && m.AccountId == member.AccountId
                && m.Status == MemberStatus.Active);

            if (duplicate)
            {
                return ValueTask.FromResult(AddMemberResult.AlreadyActive);
            }

            members.Add(member);
            return ValueTask.FromResult(AddMemberResult.Added);
        }
    }

    public void Seed(Guid courseId, Guid accountId, MemberRole role)
    {
        var member = CourseMember.Enroll(courseId, accountId, role, accountId, DateTimeOffset.UtcNow);

        lock (gate)
        {
            members.Add(member);
        }
    }
}
