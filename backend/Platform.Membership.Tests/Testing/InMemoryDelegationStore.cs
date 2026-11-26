using System.Collections.Frozen;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Tests.Testing;

/// <summary>
/// ACC-07: test dvojnik skladišta delegacija. Oponaša pravila iz EfDelegationStore
/// (bez audit zapisa nema promene). Pravu bazu proverava DelegationPostgresTests.
/// </summary>
internal sealed class InMemoryDelegationStore : IDelegationStore
{
    private readonly object gate = new();
    private readonly List<CourseDelegation> delegations = [];

    /// <summary>Simulira da Audit nije mogao da sačuva zapis.</summary>
    public bool SimulateAuditFailure { get; set; }

    /// <summary>Svaki sačuvan audit zapis: tip događaja, delegacija i akter.</summary>
    public List<(string EventType, CourseDelegation Delegation, AuditContext Context)> Audits { get; } = [];

    public IReadOnlyList<CourseDelegation> All
    {
        get
        {
            lock (gate)
            {
                return delegations.ToList();
            }
        }
    }

    /// <summary>Upis bez audita, samo za pripremu testa.</summary>
    public void Seed(CourseDelegation delegation)
    {
        lock (gate)
        {
            delegations.Add(delegation);
        }
    }

    public ValueTask<CourseDelegation?> FindAsync(
        Guid courseId, Guid delegationId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return ValueTask.FromResult(
                delegations.SingleOrDefault(d => d.Id == delegationId && d.CourseId == courseId));
        }
    }

    public ValueTask<IReadOnlyList<CourseDelegation>> ListForCourseAsync(
        Guid courseId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CourseDelegation> list = delegations
                .Where(d => d.CourseId == courseId)
                .OrderByDescending(d => d.GrantedAt)
                .ToList();
            return ValueTask.FromResult(list);
        }
    }

    public ValueTask<IReadOnlySet<string>> ActiveActionsAsync(
        Guid courseId, Guid granteeId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlySet<string> actions = delegations
                .Where(d => d.CourseId == courseId && d.GranteeId == granteeId && d.IsActiveAt(now))
                .Select(d => d.Action)
                .ToFrozenSet(StringComparer.Ordinal);
            return ValueTask.FromResult(actions);
        }
    }

    public ValueTask<bool> AddAsync(
        CourseDelegation delegation, AuditContext auditContext, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (SimulateAuditFailure)
            {
                return ValueTask.FromResult(false);
            }

            delegations.Add(delegation);
            Audits.Add((AuditEventTypes.CourseDelegationGranted, delegation, auditContext));
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<RevokeDelegationResult> RevokeAsync(
        Guid courseId,
        Guid delegationId,
        Guid revokedBy,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var delegation = delegations.SingleOrDefault(d => d.Id == delegationId && d.CourseId == courseId);
            if (delegation is null)
            {
                return ValueTask.FromResult(RevokeDelegationResult.NotFound);
            }

            if (!delegation.IsActiveAt(now))
            {
                return ValueTask.FromResult(RevokeDelegationResult.NotActive);
            }

            if (SimulateAuditFailure)
            {
                return ValueTask.FromResult(RevokeDelegationResult.AuditUnavailable);
            }

            delegation.Revoke(revokedBy, now);
            Audits.Add((AuditEventTypes.CourseDelegationRevoked, delegation, auditContext));
            return ValueTask.FromResult(RevokeDelegationResult.Revoked);
        }
    }

    public ValueTask<IReadOnlyList<CourseDelegation>> ExpiredWithoutRecordAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CourseDelegation> expired = delegations
                .Where(d => d.StateAt(now) == DelegationState.Expired && d.ExpiryRecordedAt is null)
                .OrderBy(d => d.ExpiresAt)
                .Take(maxCount)
                .ToList();
            return ValueTask.FromResult(expired);
        }
    }

    public ValueTask<bool> RecordExpiryAsync(
        Guid delegationId,
        DateTimeOffset now,
        AuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var delegation = delegations.SingleOrDefault(d => d.Id == delegationId);
            if (delegation is null
                || delegation.StateAt(now) != DelegationState.Expired
                || delegation.ExpiryRecordedAt is not null
                || SimulateAuditFailure)
            {
                return ValueTask.FromResult(false);
            }

            delegation.MarkExpiryRecorded(now);
            Audits.Add((AuditEventTypes.CourseDelegationExpired, delegation, auditContext));
            return ValueTask.FromResult(true);
        }
    }
}
