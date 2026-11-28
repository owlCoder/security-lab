using Platform.Audit.Contracts;
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

    /// <summary>Simulira da Audit nije mogao da sačuva zapis.</summary>
    public bool SimulateAuditFailure { get; set; }

    /// <summary>Simulira nedostupnu bazu: citanje baca izuzetak, kao EfMembershipStore kad baza ne radi.</summary>
    public bool SimulateUnavailable { get; set; }

    /// <summary>Audit podaci (akter, korelacioni ID) za svaki uspešan upis.</summary>
    public List<AuditContext> AuditContexts { get; } = [];

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
        if (SimulateUnavailable)
        {
            throw new InvalidOperationException("Membership database is unavailable (test).");
        }

        lock (gate)
        {
            return ValueTask.FromResult(members.SingleOrDefault(m =>
                m.CourseId == courseId && m.AccountId == accountId && m.Status == MemberStatus.Active));
        }
    }

    public ValueTask<AddMemberResult> AddAsync(
        CourseMember member,
        AuditContext auditContext,
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

            // Kao u pravoj bazi: bez audit zapisa nema upisa.
            if (SimulateAuditFailure)
            {
                return ValueTask.FromResult(AddMemberResult.AuditUnavailable);
            }

            members.Add(member);
            AuditContexts.Add(auditContext);
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