using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

public sealed class FakePolicy(
    Func<AccessRequest, CancellationToken, ValueTask<bool>> evaluate,
    string id = "fake-policy",
    string version = "v1") : IAccessPolicy
{
    private int calls;

    public string Id => id;
    public string Version => version;
    public int Calls => Volatile.Read(ref calls);

    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        return evaluate(request, cancellationToken);
    }

    public static FakePolicy Permit() => new((_, _) => ValueTask.FromResult(true));
    public static FakePolicy Refuse() => new((_, _) => ValueTask.FromResult(false));
    public static FakePolicy Throwing() => new((_, _) => throw new InvalidOperationException("boom"));
    public static FakePolicy FaultedAsync() => new(async (_, _) =>
    {
        await Task.Yield();
        throw new InvalidOperationException("async boom");
    });
    
    public static FakePolicy Cooperative() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return true;
    });
    
    public static FakePolicy NeverCompletes() => new((_, _) =>
        new ValueTask<bool>(new TaskCompletionSource<bool>().Task));
}


public sealed class BrokenIdentityPolicy(Func<string> id, Func<string> version) : IAccessPolicy
{
    public string Id => id();
    public string Version => version();
    public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);
}

public sealed class RecordingAuditWriter : IAccessAuditWriter
{
    private readonly List<(AccessRequest Request, AccessDecision Decision)> entries = [];

    public IReadOnlyList<(AccessRequest Request, AccessDecision Decision)> Entries
    {
        get { lock (entries) { return entries.ToArray(); } }
    }

    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        lock (entries) { entries.Add((request, decision)); }
        return ValueTask.CompletedTask;
    }
}

public sealed class ThrowingAuditWriter : IAccessAuditWriter
{
    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default) =>
        throw new IOException("audit sink down");
}

public sealed class NeverCompletingAuditWriter : IAccessAuditWriter
{
    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default) =>
        new(new TaskCompletionSource().Task);
}

public sealed class ThrowingCatalog : IActionCatalog
{
    public IReadOnlyCollection<ActionDefinition> Definitions => [];
    public bool Contains(string action) => throw new InvalidOperationException("catalog down");
    public bool TryGet(string action, out ActionDefinition? definition) =>
        throw new InvalidOperationException("catalog down");
}

public sealed class ThrowingRegistry : IAccessPolicyRegistry
{
    public bool TryGet(string action, out IAccessPolicy? policy) =>
        throw new InvalidOperationException("registry down");
}

public sealed class TrueButNullRegistry : IAccessPolicyRegistry
{
    public bool TryGet(string action, out IAccessPolicy? policy)
    {
        policy = null;
        return true;
    }
}
