
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-07-5: pregled svih delegacija kursa (aktivne, istekle, opozvane) za nastavnika.
/// Stanje se računa po serverskom vremenu u trenutku zahteva.
/// </summary>
public sealed class ListDelegationsService(
    IAccessDecision accessDecision,
    IDelegationStore delegations)
{
    public async ValueTask<DelegationResult> ListAsync(
        SubjectRef? caller,
        Guid courseId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                caller,
                DelegationActions.Read,
                new ResourceRef(DelegationActions.CourseResourceType, courseId.ToString("D")),
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return DelegationResult.Of(
                decision.DenialReason == AccessDenialReason.Unauthenticated
                    ? DelegationOutcome.Unauthenticated
                    : DelegationOutcome.Forbidden);
        }

        var list = await delegations.ListForCourseAsync(courseId, cancellationToken);
        return new DelegationResult(DelegationOutcome.Listed, Delegations: list);
    }
}
