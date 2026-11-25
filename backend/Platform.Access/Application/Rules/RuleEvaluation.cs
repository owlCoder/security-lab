using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Application.Rules;

public enum RuleFailure
{
    None,
    ProhibitionTriggered,
    RequirementNotMet,
    MissingValue
}

public sealed class RuleEvaluation : IEquatable<RuleEvaluation>
{
    internal RuleEvaluation(
        PolicyOutcome outcome,
        RuleFailure failure,
        string? ruleId,
        IReadOnlyList<TraceStep> steps)
    {
        Outcome = outcome;
        Failure = failure;
        RuleId = ruleId;
        Steps = steps;
    }

    public PolicyOutcome Outcome { get; }

    public RuleFailure Failure { get; }

    public string? RuleId { get; }

    public IReadOnlyList<TraceStep> Steps { get; }

    public bool IsPermit => Outcome == PolicyOutcome.Permit;

    public AccessDenialReason DenialReason => Failure switch
    {
        RuleFailure.None => AccessDenialReason.None,
        RuleFailure.MissingValue => AccessDenialReason.MissingAttribute,
        _ => AccessDenialReason.ContextMismatch
    };

    public PolicyResult ToPolicyResult() =>
        new(Outcome, DenialReason, new DecisionTrace(Steps));

    public bool Equals(RuleEvaluation? other) =>
        other is not null &&
        Outcome == other.Outcome &&
        Failure == other.Failure &&
        string.Equals(RuleId, other.RuleId, StringComparison.Ordinal) &&
        Steps.SequenceEqual(other.Steps);

    public override bool Equals(object? obj) => Equals(obj as RuleEvaluation);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Outcome);
        hash.Add(Failure);
        hash.Add(RuleId, StringComparer.Ordinal);
        foreach (var step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }
}
