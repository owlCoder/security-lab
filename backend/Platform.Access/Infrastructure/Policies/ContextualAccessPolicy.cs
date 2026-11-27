using Platform.Access.Application.Rules;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Guards;

namespace Platform.Access.Infrastructure.Policies;

public sealed class ContextualAccessPolicy : IOutcomeAccessPolicy
{
    private readonly IAccessPolicy rolePolicy;
    private readonly IEvaluationSnapshotLoader snapshotLoader;
    private readonly OwnershipScopeGuard ownershipGuard;
    private readonly RuleSet<EvaluationSnapshot>? rules;
    private readonly string action;

    public ContextualAccessPolicy(
        string action,
        IAccessPolicy rolePolicy,
        IEvaluationSnapshotLoader snapshotLoader,
        OwnershipScopeGuard ownershipGuard,
        RuleSet<EvaluationSnapshot>? rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(rolePolicy);
        ArgumentNullException.ThrowIfNull(snapshotLoader);
        ArgumentNullException.ThrowIfNull(ownershipGuard);

        this.action = action;
        this.rolePolicy = rolePolicy;
        this.snapshotLoader = snapshotLoader;
        this.ownershipGuard = ownershipGuard;
        this.rules = rules;

        if (rules is not null)
        {
            ContextualRuleIds.Validate(rules);
        }  
    }

    public string Id => $"contextual.{rolePolicy.Id}";

    public string Version => "v1";

    public async ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await EvaluateOutcomeAsync(
            request,
            cancellationToken);

        return result.Outcome == PolicyOutcome.Permit;
    }

    public async ValueTask<PolicyResult> EvaluateOutcomeAsync(
        AccessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Action, action, StringComparison.Ordinal))
        {
            return Deny(AccessDenialReason.ContextMismatch);
        }

        // G6: postojeća CourseRoleAccessPolicy mora prva da odobri.
        var roleAllowed = await rolePolicy.EvaluateAsync(
            request,
            cancellationToken);

        if (!roleAllowed)
        {
            return Deny(AccessDenialReason.NotPermitted);
        }

        // Jedan snapshot po odluci.
        var snapshot = await snapshotLoader.LoadAsync(
            request,
            cancellationToken);

        // G7: vlasništvo i scope.
        var guardResult = ownershipGuard.Evaluate(
            snapshot,
            action);

        if (guardResult.Outcome != PolicyOutcome.Permit)
        {
            return guardResult;
        }
        
        if (rules is null)
        {
            return guardResult;
        }

        // G8: contextual pravila.
        var ruleResult = rules.Evaluate(snapshot).ToPolicyResult();

        // Kombinovani trace mora imati jedinstvene ID-eve.
        var combinedSteps = new List<TraceStep>(
            guardResult.Trace.Steps.Count +
            ruleResult.Trace.Steps.Count);

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in guardResult.Trace.Steps)
        {
            if (!ids.Add(step.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate decision trace ID: '{step.Id}'.");
            }

            combinedSteps.Add(step);
        }

        foreach (var step in ruleResult.Trace.Steps)
        {
            if (!ids.Add(step.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate decision trace ID: '{step.Id}'.");
            }

            combinedSteps.Add(step);
        }

        return new PolicyResult(
            ruleResult.Outcome,
            ruleResult.Reason,
            new DecisionTrace(combinedSteps.AsReadOnly()));
    }

    private static PolicyResult Deny(AccessDenialReason reason) =>
        new(
            PolicyOutcome.Deny,
            reason,
            new DecisionTrace(Array.Empty<TraceStep>()));
}