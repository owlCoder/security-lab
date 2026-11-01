namespace Platform.Api.Modules.Access;

/// <summary>ACC-04 base checks always run before the ACC-08 context restriction.</summary>
public sealed class AccessPolicy : IAccessDecision
{
    public const string ReadAttempt = "ReadAttempt";
    public const string BaseVersion = "acc-04:v1";

    public AccessVerdict Evaluate(AccountSnapshot actor, MembershipSnapshot membership,
        AttemptSnapshot resource, string action, ContextPolicy? context, DateTimeOffset now)
    {
        AccessVerdict Deny(string reason) => new(false, reason, BaseVersion);
        if (actor.Id == Guid.Empty || !actor.IsActive) return Deny("ACCOUNT_INACTIVE");
        if (action != ReadAttempt) return Deny("UNKNOWN_ACTION");
        if (resource.Id == Guid.Empty || resource.OwnerId == Guid.Empty ||
            resource.CourseId == Guid.Empty || resource.Version < 1 ||
            !Enum.IsDefined(resource.State)) return Deny("INVALID_RESOURCE_SNAPSHOT");
        if (membership.AccountId != actor.Id || membership.CourseId != resource.CourseId ||
            !membership.IsActive || !Enum.IsDefined(membership.Role))
            return Deny("NO_ACTIVE_COURSE_MEMBERSHIP");

        var isOwner = resource.OwnerId == actor.Id;
        var isReviewer = membership.Role == CourseRole.Teacher ||
            (membership.Role == CourseRole.Assistant && membership.CanReview);
        if (!isOwner && !isReviewer) return Deny("NOT_OWNER_OR_COURSE_REVIEWER");
        if (!isOwner && resource.State == AttemptState.InProgress)
            return Deny("UNSUBMITTED_ANSWER_PRIVATE");

        // Missing/incorrect context never turns a base deny into an allow.
        if (context is null || string.IsNullOrWhiteSpace(context.Version) ||
            context.Id != resource.ContextPolicyId ||
            (context.NotBefore.HasValue && context.NotAfter.HasValue &&
             context.NotBefore >= context.NotAfter)) return Deny("INVALID_CONTEXT_POLICY");
        if (context.NotBefore.HasValue && now < context.NotBefore)
            return new(false, "CONTEXT_NOT_OPEN", context.Version);
        if (context.NotAfter.HasValue && now >= context.NotAfter)
            return new(false, "CONTEXT_EXPIRED", context.Version);

        return new(true, isOwner ? "OWNER_ACCESS" : "COURSE_REVIEW_ACCESS", context.Version);
    }
}
