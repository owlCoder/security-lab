using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionAuditEventFactory
{
    public static AuditEventDraft CreateCompletedEvent(string action, Guid operationId, Guid revisionId,
        Guid courseId, Guid authorId, string hash, Guid? laboratoryId, int? number)
    {
        var type = action switch
        {
            "submit" => AuditEventTypes.RevisionSubmitted,
            "approve" => AuditEventTypes.RevisionApproved,
            "return" => AuditEventTypes.RevisionReturned,
            _ => throw new ArgumentException("Unknown revision operation.", nameof(action))
        };
        var details = new Dictionary<string, string>
        {
            ["authorId"] = authorId.ToString("D"),
            ["operationId"] = operationId.ToString("D"),
            ["contentHash"] = hash
        };
        if (laboratoryId is Guid lab) details["laboratoryId"] = lab.ToString("D");
        if (number is int revisionNumber)
            details["revisionNumber"] = revisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new AuditEventDraft(type, AuditOutcome.Success, courseId,
            new AuditResourceRef(AuditResourceTypes.Revision, revisionId.ToString("D")), details, operationId);
    }

    public static AuditEventDraft? CreateDeniedEvent(
        AccessRequest request,
        AccessDecision decision)
    {
        if (decision.Outcome != AccessOutcome.Deny)
        {
            return null;
        }

        if (!RevisionActions.IsKnown(request.Action))
        {
            return null;
        }

        var details = new Dictionary<string, string>
        {
            ["action"] = request.Action,
            ["denialReason"] = ToReasonCode(decision.DenialReason)
        };

        if (!string.IsNullOrWhiteSpace(decision.PolicyId))
        {
            details["policyId"] = decision.PolicyId;
        }

        if (!string.IsNullOrWhiteSpace(decision.PolicyVersion) &&
            System.Text.RegularExpressions.Regex.IsMatch(
                decision.PolicyVersion, @"^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}$"))
        {
            details["policyVersion"] = decision.PolicyVersion;
        }

        return new AuditEventDraft(
            AuditEventTypes.AccessDenied,
            AuditOutcome.Denied,
            Details: details);
    }

    private static string ToReasonCode(AccessDenialReason reason) => reason switch
    {
        AccessDenialReason.Unauthenticated => "unauthenticated",
        AccessDenialReason.AccountNotActive => "account_not_active",
        AccessDenialReason.UnknownAction => "unknown_action",
        AccessDenialReason.NoPolicy => "no_policy",
        AccessDenialReason.PolicyDenied => "policy_denied",
        AccessDenialReason.PolicyError => "policy_error",
        _ => "policy_error"
    };
}

