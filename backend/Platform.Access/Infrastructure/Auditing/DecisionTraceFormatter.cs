using System.Text.RegularExpressions;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Auditing;

public interface IDecisionTraceFormatter
{
    string Format(DecisionTrace? trace);
}

public sealed partial class DecisionTraceFormatter : IDecisionTraceFormatter
{
    private const int MaxSteps = 16;
    private const string Invalid = "invalid";

    [GeneratedRegex(@"^[a-z0-9._:-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StepIdPattern();

    public string Format(DecisionTrace? trace)
    {
        var steps = trace?.Steps;
        if (steps is null || steps.Count == 0)
        {
            return "none";
        }

        var parts = steps.Take(MaxSteps).Select(FormatStep).ToList();
        if (steps.Count > MaxSteps)
        {
            parts.Add("truncated");
        }

        return string.Join(',', parts);
    }

    private static string FormatStep(TraceStep step)
    {
        var id = step.Id is not null && StepIdPattern().IsMatch(step.Id) ? step.Id : Invalid;
        return $"{id}={ResultName(step.Result)}";
    }

    private static string ResultName(TraceResult result) => result switch
    {
        TraceResult.Passed => "passed",
        TraceResult.Failed => "failed",
        TraceResult.Indeterminate => "indeterminate",
        TraceResult.Skipped => "skipped",
        _ => Invalid
    };
}
