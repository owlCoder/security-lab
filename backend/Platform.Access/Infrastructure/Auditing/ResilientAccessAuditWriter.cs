using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed partial class ResilientAccessAuditWriter(
    IAccessAuditWriter inner,
    AccessAuditOptions options,
    TimeProvider clock,
    ILogger<ResilientAccessAuditWriter> logger) : IAccessAuditWriter
{
    private const string FailedReason = "failed";
    private const string TimedOutReason = "timed_out";

    public async ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        using var cts = new CancellationTokenSource(options.WriteTimeout, clock);
        Task? write = null;

        try
        {
            write = inner.WriteAsync(request, decision, cts.Token).AsTask();
            await write.WaitAsync(options.WriteTimeout, clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Stop(cts, write);
            RaiseAlarm(request, TimedOutReason, null);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            RaiseAlarm(request, TimedOutReason, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            RaiseAlarm(request, FailedReason, ex.GetType().Name);
        }
    }

    private static void Stop(CancellationTokenSource cts, Task? write)
    {
        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
        }

        write?.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RaiseAlarm(AccessRequest? request, string reason, string? exceptionType)
    {
        try
        {
            var rawCorrelationId = request?.CorrelationId;
            var correlationId =
                string.IsNullOrEmpty(rawCorrelationId) ? AccessAuditRecord.NoValue
                : AuditIdentifier.IsValid(rawCorrelationId, options.MaxValueLength) ? rawCorrelationId
                : AccessAuditRecord.Invalid;
            var type = AuditValueSanitizer.Clean(exceptionType, options.MaxValueLength);

            LogAuditFailure(
                logger,
                reason,
                type.Length == 0 ? AccessAuditRecord.NoValue : type,
                correlationId);
        }
        catch (Exception)
        {
        }
    }

    [LoggerMessage(
        EventId = 6103,
        EventName = "access.audit.failure",
        Level = LogLevel.Error,
        Message = "access.audit.failure reason={Reason} exceptionType={ExceptionType} correlationId={CorrelationId}")]
    private static partial void LogAuditFailure(
        ILogger logger, string reason, string exceptionType, string correlationId);
}
