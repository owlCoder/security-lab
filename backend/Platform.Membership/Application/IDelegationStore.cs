using Platform.Audit.Contracts;
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07: čuvanje delegacija. Zapis se nikad ne briše. Dodela, opoziv i zapis o isteku
/// idu u istoj transakciji sa audit zapisom (S11-06): bez audit zapisa nema promene.
/// </summary>
public interface IDelegationStore
{
    /// <summary>Delegacija iz tog kursa; null ako ne postoji ili pripada drugom kursu.</summary>
    ValueTask<CourseDelegation?> FindAsync(
        Guid courseId, Guid delegationId, CancellationToken cancellationToken = default);

    /// <summary>Sve delegacije kursa (i istekle i opozvane), najnovije prve.</summary>
    ValueTask<IReadOnlyList<CourseDelegation>> ListForCourseAsync(
        Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>Radnje koje korisnik u kursu ima preko delegacija koje važe u trenutku now.</summary>
    ValueTask<IReadOnlySet<string>> ActiveActionsAsync(
        Guid courseId, Guid granteeId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Upis nove delegacije i audit zapisa. false = audit nije sačuvan, pa nema ni upisa.</summary>
    ValueTask<bool> AddAsync(
        CourseDelegation delegation, AuditContext auditContext, CancellationToken cancellationToken = default);

    /// <summary>Opoziv aktivne delegacije i audit zapis, u jednoj transakciji.</summary>
    ValueTask<RevokeDelegationResult> RevokeAsync(
        Guid courseId,
        Guid delegationId,
        Guid revokedBy,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default);

    /// <summary>Istekle (ne opozvane) delegacije čiji istek još nije upisan u audit.</summary>
    ValueTask<IReadOnlyList<CourseDelegation>> ExpiredWithoutRecordAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>Upis isteka u audit, najviše jednom po delegaciji. true = upisano sada.</summary>
    ValueTask<bool> RecordExpiryAsync(
        Guid delegationId,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default);
}

public enum RevokeDelegationResult
{
    Revoked,
    NotFound,
    NotActive,
    AuditUnavailable
}
