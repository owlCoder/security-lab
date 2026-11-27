using System.Text.RegularExpressions;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Audit.Contracts;

namespace Platform.Api.Security.AccessAudit;

public interface IAccessDeniedAuditMapper
{
    AuditEventDraft CreateDraft(AccessRequest request, AccessDecision decision);

    AuditContext CreateContext(AccessRequest request, AccessDecision decision);
}

public sealed partial class AccessDeniedAuditMapper : IAccessDeniedAuditMapper
{
    [GeneratedRegex(@"^[a-z][a-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    public AuditEventDraft CreateDraft(AccessRequest request, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var details = new Dictionary<string, string>
        {
            ["denialReason"] = DenialReasonOf(decision.DenialReason),
        };

        if (ActionId.IsValid(request.Action))
        {
            details["action"] = request.Action;
        }

        if (decision.PolicyId is { } policyId && PolicyIdPattern().IsMatch(policyId))
        {
            details["policyId"] = policyId;
        }

        if (decision.PolicyVersion is { } version && PolicyVersionPattern().IsMatch(version))
        {
            details["policyVersion"] = version;
        }

        return new AuditEventDraft(
            AuditEventTypes.AccessDenied,
            AuditOutcome.Denied,
            CourseId: CourseOf(request.Resource),
            Details: details);
    }

    public AuditContext CreateContext(AccessRequest request, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        AuditActor actor;
        if (request.Subject is null)
        {
            actor = AuditActor.Anonymous;
        }
        else if (Guid.TryParse(request.Subject.Id, out var userId) && userId != Guid.Empty)
        {
            actor = AuditActor.ForUser(userId);
        }
        else
        {
            throw new InvalidOperationException("A subject with an invalid user ID cannot be audited.");
        }

        return new AuditContext(actor, decision.CorrelationId);
    }

    // A course resource id is already normalised to "D" format by the access middleware.
    private static Guid? CourseOf(ResourceRef? resource) =>
        resource is { Type: "course" }
        && Guid.TryParseExact(resource.Id, "D", out var courseId)
        && courseId != Guid.Empty
            ? courseId
            : null;

    private static string DenialReasonOf(AccessDenialReason reason) => reason switch
    {
        AccessDenialReason.Unauthenticated => "unauthenticated",
        AccessDenialReason.AccountNotActive => "account_not_active",
        AccessDenialReason.UnknownAction => "unknown_action",
        AccessDenialReason.NoPolicy => "no_policy",
        AccessDenialReason.PolicyDenied => "policy_denied",
        _ => "policy_error",
    };
}
