using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Services.Identity;
using Platform.LabLocalization.Application;

namespace Platform.Api.Modules.LabLocalization.Infrastructure;

public sealed class AccessDecisionLocalizationAccess(
    IAccessDecision access,
    LabCaller callers,
    IHttpContextAccessor httpContext) : ILocalizationAccess
{
    public async Task<bool> IsAllowedAsync(
        Guid actorId, LocalizationAction action, Guid resourceId, CancellationToken ct = default)
    {
        (string? actionId, string? resourceType) = action switch
        {
            LocalizationAction.ReadContent => (LabLocalizationActions.Read, LabLocalizationActions.LaboratoryResource),
            LocalizationAction.AddTranslation => (LabLocalizationActions.Translate, LabLocalizationActions.RevisionResource),
            _ => ((string?)null, (string?)null),
        };

        if (actionId is null || resourceType is null || actorId == Guid.Empty || resourceId == Guid.Empty)
        {
            return false;
        }

        // Stanje naloga dolazi iz sesije; actorId samo mora da se poklopi sa njom.
        SubjectRef? subject = await callers.GetSubjectAsync(ct);
        if (subject is null || subject.Id != actorId.ToString("D"))
        {
            return false;
        }

        var decision = await access.DecideAsync(
            new AccessRequest(
                subject,
                actionId,
                new ResourceRef(resourceType, resourceId.ToString("D")),
                httpContext.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N")),
            ct);

        return decision.Outcome == AccessOutcome.Allow;
    }
}
