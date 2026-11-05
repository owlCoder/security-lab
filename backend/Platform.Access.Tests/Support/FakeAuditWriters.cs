using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Support;

internal sealed class SyncThrowingWriter(Exception exception) : IAccessAuditWriter
{
    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default) =>
        throw exception;
}

internal sealed class AsyncThrowingWriter(Exception exception) : IAccessAuditWriter
{
    public async ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw exception;
    }
}

internal sealed class HangingWriter : IAccessAuditWriter
{
    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default) =>
        new(new TaskCompletionSource().Task);
}

internal sealed class TokenObservingWriter : IAccessAuditWriter
{
    private readonly TaskCompletionSource<bool> _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Cancelled => _cancelled.Task;

    public async ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _cancelled.TrySetResult(true);
            throw;
        }
    }
}

internal sealed class TokenRecordingWriter : IAccessAuditWriter
{
    public bool Called { get; private set; }

    public bool TokenWasCancelled { get; private set; }

    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        Called = true;
        TokenWasCancelled = cancellationToken.IsCancellationRequested;
        return ValueTask.CompletedTask;
    }
}
