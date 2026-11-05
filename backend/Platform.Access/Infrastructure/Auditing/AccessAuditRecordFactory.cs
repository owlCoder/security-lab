using System.Text.RegularExpressions;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed partial class AccessAuditRecordFactory(TimeProvider clock, AccessAuditOptions options)
{
    [GeneratedRegex(@"^[a-z][a-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    public AccessAuditRecord Create(AccessRequest request, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var denied = decision.Outcome != AccessOutcome.Allow;

        return new AccessAuditRecord(
            Time: clock.GetUtcNow(),
            EventType: denied ? AccessAuditRecord.DeniedEventType : AccessAuditRecord.PrivilegedAllowedEventType,
            Actor: ActorOf(request.Subject),
            Action: ActionOf(request.Action),
            Target: TargetOf(request.Resource),
            Outcome: denied ? "deny" : "allow",
            CorrelationId: IdentifierOrNoValue(request.CorrelationId),
            DenialReason: denied ? DenialReasonOf(decision.DenialReason) : null,
            PolicyId: decision.PolicyId is null ? null : Matching(decision.PolicyId, PolicyIdPattern()),
            PolicyVersion: Matching(decision.PolicyVersion, PolicyVersionPattern()));
    }

    internal static string DenialReasonOf(AccessDenialReason reason) => reason switch
    {
        AccessDenialReason.Unauthenticated => "unauthenticated",
        AccessDenialReason.AccountNotActive => "account_not_active",
        AccessDenialReason.UnknownAction => "unknown_action",
        AccessDenialReason.NoPolicy => "no_policy",
        _ => "policy_error",
    };

    private string ActorOf(SubjectRef? subject)
    {
        if (subject is null)
        {
            return AccessAuditRecord.Anonymous;
        }

        return AuditIdentifier.IsValid(subject.Id, options.MaxValueLength) ? subject.Id : AccessAuditRecord.Invalid;
    }

    private static string ActionOf(string? action) =>
        action is not null && ActionId.IsValid(action) && !action.Any(char.IsControl)
            ? action
            : AccessAuditRecord.Invalid;

    private string TargetOf(ResourceRef? resource)
    {
        if (resource is null)
        {
            return AccessAuditRecord.NoValue;
        }

        var target = $"{IdentifierOrNoValue(resource.Type)}:{IdentifierOrNoValue(resource.Id)}";

        return target.Length <= options.MaxValueLength ? target : AccessAuditRecord.Invalid;
    }

    private string Matching(string? value, Regex pattern) =>
        value is not null && value.Length <= options.MaxValueLength && pattern.IsMatch(value)
            ? value
            : AccessAuditRecord.Invalid;

    private string IdentifierOrNoValue(string? value) =>
        string.IsNullOrEmpty(value) ? AccessAuditRecord.NoValue
        : AuditIdentifier.IsValid(value, options.MaxValueLength) ? value
        : AccessAuditRecord.Invalid;
}
