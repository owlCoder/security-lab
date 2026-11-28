using Platform.Audit.Contracts;
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// Čuvanje članstava. Pravilo "nema duplog aktivnog upisa" čuva baza (jedinstveni indeks).
/// </summary>
public interface IMembershipStore
{
    ValueTask<CourseMember?> FindActiveAsync(
        Guid courseId, Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upis članstva i audit zapisa (AUD-01, spec. 2.1) u jednoj transakciji.
    /// Ako audit zapis ne uspe, članstvo se ne upisuje.
    /// </summary>
    ValueTask<AddMemberResult> AddAsync(
        CourseMember member, AuditContext auditContext, CancellationToken cancellationToken = default);
}

public enum AddMemberResult
{
    Added,
    AlreadyActive,
    AuditUnavailable
}