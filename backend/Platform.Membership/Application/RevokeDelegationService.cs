
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07-5: opoziv delegacije pre isteka. Pristup prestaje na sledećem zahtevu, a zapis ostaje
/// (ko je i kada opozvao). Delegacija drugog kursa daje isti odgovor kao nepostojeća.
/// </summary>
public sealed class RevokeDelegationService(
    IAccessDecision accessDecision,
    IDelegationStore delegations,
    TimeProvider clock)
{
    public async ValueTask<DelegationResult> RevokeAsync(
        SubjectRef? caller,
        Guid courseId,
        Guid delegationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                caller,
                DelegationActions.Revoke,
                new ResourceRef(DelegationActions.CourseResourceType, courseId.ToString("D")),
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return DelegationResult.Of(decision.DenialReason == AccessDenialReason.Unauthenticated
                ? DelegationOutcome.Unauthenticated
                : DelegationOutcome.Forbidden);
        }

        if (caller is null || !Guid.TryParse(caller.Id, out var actorId) || actorId == Guid.Empty)
        {
            return DelegationResult.Of(DelegationOutcome.Forbidden);
        }

        if (delegationId == Guid.Empty)
        {
            return DelegationResult.Of(DelegationOutcome.NotFound);
        }

        var result = await delegations.RevokeAsync(
            courseId,
            delegationId,
            actorId,
            clock.GetUtcNow(),
            new AuditContext(AuditActor.ForUser(actorId), correlationId),
            cancellationToken);

        return DelegationResult.Of(result switch
        {
            RevokeDelegationResult.Revoked => DelegationOutcome.Revoked,
            RevokeDelegationResult.NotFound => DelegationOutcome.NotFound,
            RevokeDelegationResult.NotActive => DelegationOutcome.NotActive,
            _ => DelegationOutcome.Unavailable
        });
    }
}
