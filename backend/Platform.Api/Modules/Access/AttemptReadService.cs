namespace Platform.Api.Modules.Access;

public sealed class AttemptReadService(IAccountReader accounts, IMembershipReader memberships,
    IAttemptReader attempts, IContextPolicyReader contexts, IAccessDecision policy,
    IAuditWriter audit, TimeProvider clock)
{
    public ReadResult Read(Guid? actorId, Guid attemptId, Guid correlationId)
    {
        Guid? courseId = null;
        ReadResult Finish(int status, string reason, string version = AccessPolicy.BaseVersion,
            AttemptView? value = null)
        {
            audit.Write(new(clock.GetUtcNow(), correlationId, actorId, AccessPolicy.ReadAttempt,
                attemptId, courseId, status == 200 ? "ALLOW" : "DENY", reason, version));
            return new(status, value);
        }
        if (actorId is null || actorId == Guid.Empty)
            return Finish(401, "UNAUTHENTICATED");
        if (attemptId == Guid.Empty) return Finish(400, "INVALID_RESOURCE_ID");
        try
        {
            var account = accounts.FindAccount(actorId.Value);
            if (account.Status != PortStatus.Ok || account.Value is null ||
                account.Value.Id != actorId || !account.Value.IsActive)
                return Finish(404, account.Status == PortStatus.Unavailable ?
                    "IDENTITY_UNAVAILABLE" : "ACCOUNT_INACTIVE_OR_MISSING");
            var resource = attempts.FindAttempt(attemptId);
            if (resource.Status != PortStatus.Ok || resource.Value is null)
                return Finish(404, resource.Status == PortStatus.Unavailable ?
                    "ATTEMPT_STORE_UNAVAILABLE" : "RESOURCE_NOT_FOUND");
            if (resource.Value.Id != attemptId) return Finish(404, "RESOURCE_ID_MISMATCH");
            courseId = resource.Value.CourseId;
            var membership = memberships.FindMembership(actorId.Value, courseId.Value);
            if (membership.Status != PortStatus.Ok || membership.Value is null)
                return Finish(404, membership.Status == PortStatus.Unavailable ?
                    "MEMBERSHIP_UNAVAILABLE" : "MEMBERSHIP_NOT_FOUND");
            var context = contexts.FindPolicy(resource.Value.ContextPolicyId);
            if (context.Status != PortStatus.Ok || context.Value is null)
                return Finish(404, context.Status == PortStatus.Unavailable ?
                    "CONTEXT_UNAVAILABLE" : "CONTEXT_NOT_FOUND");
            var verdict = policy.Evaluate(account.Value, membership.Value, resource.Value,
                AccessPolicy.ReadAttempt, context.Value, clock.GetUtcNow());
            if (!verdict.Allowed) return Finish(404, verdict.ReasonCode, verdict.PolicyVersion);
            var answer = attempts.ReadAnswer(resource.Value);
            if (answer.Status != PortStatus.Ok || answer.Value is null ||
                answer.Value.Id != resource.Value.Id || answer.Value.Version != resource.Value.Version)
                return Finish(404, "ANSWER_SNAPSHOT_UNAVAILABLE", verdict.PolicyVersion);
            return Finish(200, verdict.ReasonCode, verdict.PolicyVersion, answer.Value);
        }
        catch (TimeoutException)
        {
            return Finish(404, "DEPENDENCY_TIMEOUT");
        }
    }
}
