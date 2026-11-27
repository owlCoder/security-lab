using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public sealed class ResultsSensitiveReadPolicy(
    IEvaluationSnapshotLoader snapshotLoader,
    IElevatedGrantValidator grantValidator) : IOutcomeAccessPolicy
{
    private const string ActionMatch = "action.match";
    private const string ResourceCourse = "resource.course";
    private const string SubjectId = "subject.id";
    private const string SubjectAdministrator = "subject.administrator";
    private const string GrantHolder = "grant.holder";

    public string Id => "elevated-grant.results.sensitive.read";

    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await EvaluateOutcomeAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Outcome == PolicyOutcome.Permit;
    }

    public async ValueTask<PolicyResult> EvaluateOutcomeAsync(
        AccessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var steps = new List<TraceStep>();

        if (!string.Equals(request.Action, AccessActions.ResultsSensitiveRead.Id, StringComparison.Ordinal))
        {
            return Deny(AccessDenialReason.ContextMismatch, steps, ActionMatch);
        }

        steps.Add(new TraceStep(ActionMatch, TraceResult.Passed));

        var snapshot = await snapshotLoader.LoadAsync(request, cancellationToken).ConfigureAwait(false);

        if (snapshot.Resource?.CourseId is not { } courseId || courseId == Guid.Empty)
        {
            return Deny(AccessDenialReason.MissingAttribute, steps, ResourceCourse);
        }

        steps.Add(new TraceStep(ResourceCourse, TraceResult.Passed));

        if (!Guid.TryParseExact(snapshot.Subject.Id, "D", out var holderId) || holderId == Guid.Empty)
        {
            return Deny(AccessDenialReason.MissingAttribute, steps, SubjectId);
        }

        steps.Add(new TraceStep(SubjectId, TraceResult.Passed));

        if (snapshot.Subject.SystemRole != SystemRole.Administrator)
        {
            return Deny(AccessDenialReason.NotPermitted, steps, SubjectAdministrator);
        }

        steps.Add(new TraceStep(SubjectAdministrator, TraceResult.Passed));

        var hasValidGrant = snapshot.Grants.Any(grant =>
            grantValidator.IsValid(grant, holderId, request.Action, snapshot.Resource, snapshot.Now));
        if (!hasValidGrant)
        {
            return Deny(AccessDenialReason.NotPermitted, steps, GrantHolder);
        }

        steps.Add(new TraceStep(GrantHolder, TraceResult.Passed));
        return new PolicyResult(PolicyOutcome.Permit, AccessDenialReason.None, new DecisionTrace(steps));
    }

    private static PolicyResult Deny(
        AccessDenialReason reason,
        List<TraceStep> passedSteps,
        string failedStepId)
    {
        passedSteps.Add(new TraceStep(failedStepId, TraceResult.Failed));
        return new PolicyResult(PolicyOutcome.Deny, reason, new DecisionTrace(passedSteps));
    }
}
