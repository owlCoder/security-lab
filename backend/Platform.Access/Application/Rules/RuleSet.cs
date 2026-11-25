using Platform.Access.Application.Services;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Application.Rules;

public sealed class RuleSet<TContext>
{
    private readonly Rule<TContext>[] requirements;
    private readonly Rule<TContext>[] prohibitions;

    public RuleSet(
        IEnumerable<Rule<TContext>> requirements,
        IEnumerable<Rule<TContext>>? prohibitions = null,
        RuleLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        limits ??= RuleLimits.Default;
        limits.Validate();

        this.requirements = CopyRules(requirements);
        this.prohibitions = prohibitions is null ? [] : CopyRules(prohibitions);

        if (this.requirements.Length == 0)
        {
            throw new AccessConfigurationException(
                "A rule set must contain at least one requirement; a set without requirements would permit everything.");
        }

        if (this.requirements.Length + this.prohibitions.Length > limits.MaxRules)
        {
            throw new AccessConfigurationException($"A rule set may contain at most {limits.MaxRules} rules.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in this.requirements.Concat(this.prohibitions))
        {
            if (!ids.Add(rule.Id))
            {
                throw new AccessConfigurationException($"Rule id '{rule.Id}' is used more than once.");
            }
        }

        var nodes = 0;
        foreach (var rule in this.requirements.Concat(this.prohibitions))
        {
            Measure(rule.Condition, depth: 1, ref nodes, limits);
        }
    }

    public IReadOnlyList<Rule<TContext>> Requirements => requirements;

    public IReadOnlyList<Rule<TContext>> Prohibitions => prohibitions;

    public RuleEvaluation Evaluate(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? triggered = null;
        string? unmet = null;
        string? unknown = null;
        var steps = new List<TraceStep>(requirements.Length + prohibitions.Length);

        foreach (var rule in prohibitions)
        {
            switch (TriLogic.Normalize(rule.Condition.Evaluate(context)))
            {
                case Tri.True:
                    triggered = Smallest(triggered, rule.Id);
                    steps.Add(new TraceStep(rule.Id, TraceResult.Failed));
                    break;
                case Tri.False:
                    steps.Add(new TraceStep(rule.Id, TraceResult.Passed));
                    break;
                default:
                    unknown = Smallest(unknown, rule.Id);
                    steps.Add(new TraceStep(rule.Id, TraceResult.Indeterminate));
                    break;
            }
        }

        foreach (var rule in requirements)
        {
            switch (TriLogic.Normalize(rule.Condition.Evaluate(context)))
            {
                case Tri.True:
                    steps.Add(new TraceStep(rule.Id, TraceResult.Passed));
                    break;
                case Tri.False:
                    unmet = Smallest(unmet, rule.Id);
                    steps.Add(new TraceStep(rule.Id, TraceResult.Failed));
                    break;
                default:
                    unknown = Smallest(unknown, rule.Id);
                    steps.Add(new TraceStep(rule.Id, TraceResult.Indeterminate));
                    break;
            }
        }

        steps.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
        var trace = steps.AsReadOnly();

        if (triggered is not null)
        {
            return new RuleEvaluation(PolicyOutcome.Deny, RuleFailure.ProhibitionTriggered, triggered, trace);
        }

        if (unmet is not null)
        {
            return new RuleEvaluation(PolicyOutcome.Deny, RuleFailure.RequirementNotMet, unmet, trace);
        }

        if (unknown is not null)
        {
            return new RuleEvaluation(PolicyOutcome.Indeterminate, RuleFailure.MissingValue, unknown, trace);
        }

        return new RuleEvaluation(PolicyOutcome.Permit, RuleFailure.None, null, trace);
    }

    private static Rule<TContext>[] CopyRules(IEnumerable<Rule<TContext>> rules)
    {
        var copy = rules.ToArray();
        if (copy.Any(rule => rule is null))
        {
            throw new AccessConfigurationException("A rule set must not contain a null rule.");
        }

        return copy;
    }

   private static void Measure(ICondition<TContext> condition, int depth, ref int nodes, RuleLimits limits)
    {
        if (condition is null)
        {
            throw new AccessConfigurationException("A rule condition must not contain a null condition.");
        }

        if (depth > limits.MaxDepth)
        {
            throw new AccessConfigurationException($"A rule condition may be at most {limits.MaxDepth} levels deep.");
        }

        if (++nodes > limits.MaxNodes)
        {
            throw new AccessConfigurationException($"A rule set may contain at most {limits.MaxNodes} condition nodes.");
        }

        var children = condition.Children
            ?? throw new AccessConfigurationException("A condition must expose its children (null is not allowed).");

        foreach (var child in children)
        {
            Measure(child, depth + 1, ref nodes, limits);
        }
    }

    private static string Smallest(string? current, string candidate) =>
        current is null || string.CompareOrdinal(candidate, current) < 0 ? candidate : current;
}
