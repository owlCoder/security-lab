using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

public sealed class FakeOutcomePolicy(
    Func<AccessRequest, CancellationToken, ValueTask<PolicyResult>> evaluate,
    string id = "fake-outcome-policy",
    string version = "v1") : IOutcomeAccessPolicy
{
    private int boolCalls;
    private int outcomeCalls;

    public string Id => id;
    public string Version => version;
    public int BoolCalls => Volatile.Read(ref boolCalls);
    public int OutcomeCalls => Volatile.Read(ref outcomeCalls);

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref boolCalls);
        return ValueTask.FromResult(true);
    }

    public ValueTask<PolicyResult> EvaluateOutcomeAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref outcomeCalls);
        return evaluate(request, cancellationToken);
    }

    public static PolicyResult Result(PolicyOutcome outcome, AccessDenialReason reason) =>
        new(outcome, reason, new DecisionTrace(Array.Empty<TraceStep>()));

    public static FakeOutcomePolicy Returning(PolicyOutcome outcome, AccessDenialReason reason) =>
        new((_, _) => ValueTask.FromResult(Result(outcome, reason)));
}
