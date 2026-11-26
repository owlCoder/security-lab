using Platform.Audit.Contracts;
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07 / AUD-01: događaji delegacije iz kataloga Tima 12 (AuditEventCatalog).
/// Kurs je obavezan, resurs je nalog asistenta, a detalji su samo action i delegationId.
/// Istek beleži pozadinski posao kao Service akter (S11-08).
/// </summary>
public static class DelegationAuditEvents
{
    /// <summary>Stalni ID pozadinskog posla koji beleži istek (AuditActor.ForService).</summary>
    public static readonly Guid ExpiryServiceId = Guid.Parse("acc07000-0000-0000-0000-000000000001");

    public static AuditEventDraft Granted(CourseDelegation delegation) =>
        Draft(AuditEventTypes.CourseDelegationGranted, delegation);

    public static AuditEventDraft Revoked(CourseDelegation delegation) =>
        Draft(AuditEventTypes.CourseDelegationRevoked, delegation);

    public static AuditEventDraft Expired(CourseDelegation delegation) =>
        Draft(AuditEventTypes.CourseDelegationExpired, delegation);

    private static AuditEventDraft Draft(string eventType, CourseDelegation delegation)
    {
        ArgumentNullException.ThrowIfNull(delegation);
        return new AuditEventDraft(
            eventType,
            AuditOutcome.Success,
            CourseId: delegation.CourseId,
            Resource: new AuditResourceRef(AuditResourceTypes.Account, delegation.GranteeId.ToString("D")),
            Details: new Dictionary<string, string>
            {
                ["action"] = delegation.Action,
                ["delegationId"] = delegation.Id.ToString("D")
            });
    }
}
