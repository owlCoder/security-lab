using Platform.Access.Domain.Enums;

namespace Platform.Access.Domain.Models;

public sealed record AccessDecision(
    AccessOutcome Outcome,
    AccessDenialReason DenialReason,
    string? PolicyId,
    string PolicyVersion,
    string CorrelationId)
{
    private const string InitialPolicyVersion = "v0";

    public static AccessDecision Deny(
        AccessDenialReason reason,
        string correlationId,
        string policyVersion = InitialPolicyVersion)
        => new(AccessOutcome.Deny, reason, null, policyVersion, correlationId);

    public static AccessDecision Allow(
        string policyId,
        string correlationId,
        string policyVersion = InitialPolicyVersion)
        => new(AccessOutcome.Allow, AccessDenialReason.None, policyId, policyVersion, correlationId);
}
