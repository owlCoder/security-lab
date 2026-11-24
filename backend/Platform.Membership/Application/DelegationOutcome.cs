using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>ACC-07: ishodi dodele, opoziva i pregleda delegacija.</summary>
public enum DelegationOutcome
{
    Granted,
    Revoked,
    Listed,
    Unauthenticated,
    Forbidden,
    InvalidInput,
    CourseNotFound,
    CourseArchived,
    GranteeNotEligible,
    NotFound,
    NotActive,
    Unavailable
}

public sealed record DelegationResult(
    DelegationOutcome Outcome,
    CourseDelegation? Delegation = null,
    IReadOnlyList<CourseDelegation>? Delegations = null)
{
    public static DelegationResult Of(DelegationOutcome outcome) => new(outcome);
}
