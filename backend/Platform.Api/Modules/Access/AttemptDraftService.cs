namespace Platform.Api.Modules.Access;

public sealed class AttemptDraftService(IAccountReader accounts, IMembershipReader memberships,
    IAttemptReader attempts, IAttemptDraftWriter drafts, IContextPolicyReader contexts,
    IAccessDecision policy, IAuditWriter audit, TimeProvider clock)
{
    public const int MaxAnswerLength = 4000;

    public UpdateResult Update(Guid? actorId, Guid attemptId, UpdateDraftRequest request, Guid correlationId)
    {
        Guid? courseId = null;
        int? before = null;
        UpdateResult Finish(int status, string reason, string version = AccessPolicy.BaseVersion,
            AttemptView? value = null)
        {
            audit.Write(new(clock.GetUtcNow(), correlationId, actorId, AccessPolicy.UpdateDraftAnswer,
                attemptId, courseId, status == 200 ? "ALLOW" : status == 409 ? "CONFLICT" : "DENY",
                reason, version, before, status == 200 ? value?.Version : null));
            return new(status, value);
        }

        if (actorId is null || actorId == Guid.Empty) return Finish(401, "UNAUTHENTICATED");
        if (attemptId == Guid.Empty || string.IsNullOrWhiteSpace(request.Answer) ||
            request.Answer.Length > MaxAnswerLength || request.ExpectedVersion < 1)
            return Finish(400, "INVALID_UPDATE_INPUT");
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
            before = resource.Value.Version;
            var membership = memberships.FindMembership(actorId.Value, courseId.Value);
            if (membership.Status != PortStatus.Ok || membership.Value is null)
                return Finish(404, membership.Status == PortStatus.Unavailable ?
                    "MEMBERSHIP_UNAVAILABLE" : "MEMBERSHIP_NOT_FOUND");
            var context = contexts.FindPolicy(resource.Value.ContextPolicyId);
            if (context.Status != PortStatus.Ok || context.Value is null)
                return Finish(404, context.Status == PortStatus.Unavailable ?
                    "CONTEXT_UNAVAILABLE" : "CONTEXT_NOT_FOUND");
            var verdict = policy.Evaluate(account.Value, membership.Value, resource.Value,
                AccessPolicy.UpdateDraftAnswer, context.Value, clock.GetUtcNow());
            if (!verdict.Allowed) return Finish(404, verdict.ReasonCode, verdict.PolicyVersion);
            if (request.ExpectedVersion != resource.Value.Version)
                return Finish(409, "RESOURCE_VERSION_CONFLICT", verdict.PolicyVersion);

            var result = drafts.TryUpdateDraft(resource.Value, request.ExpectedVersion, request.Answer);
            return result.Status switch
            {
                DraftWriteStatus.Updated when result.Resource is not null &&
                    result.Resource.Id == attemptId && result.Resource.Version == resource.Value.Version + 1
                    => Finish(200, verdict.ReasonCode, verdict.PolicyVersion, result.Resource),
                DraftWriteStatus.Conflict => Finish(409, "RESOURCE_VERSION_CONFLICT", verdict.PolicyVersion),
                DraftWriteStatus.Unavailable => Finish(404, "ATTEMPT_STORE_UNAVAILABLE", verdict.PolicyVersion),
                _ => Finish(404, "DRAFT_SNAPSHOT_UNAVAILABLE", verdict.PolicyVersion)
            };
        }
        catch (TimeoutException)
        {
            return Finish(404, "DEPENDENCY_TIMEOUT");
        }
    }
}
