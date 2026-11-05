using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed partial class AccessAuditFlushService(
    AggregatingAccessAuditSink sink,
    AccessAuditOptions options,
    TimeProvider clock,
    ILogger<AccessAuditFlushService> logger) : BackgroundService
{
    private const string PeriodicPhase = "periodic";
    private const string ShutdownPhase = "shutdown";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.FlushInterval, clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushAsync(sink.FlushExpiredAsync, PeriodicPhase, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await FlushAsync(sink.FlushAllAsync, ShutdownPhase, cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushAsync(
        Func<CancellationToken, ValueTask> flush,
        string phase,
        CancellationToken cancellationToken)
    {
        try
        {
            await flush(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RaiseAlarm(phase, ex.GetType().Name);
        }
    }

    private void RaiseAlarm(string phase, string exceptionType)
    {
        try
        {
            LogFlushFailure(logger, phase, exceptionType);
        }
        catch (Exception)
        {
        }
    }

    [LoggerMessage(
        EventId = 6104,
        EventName = "access.audit.flush.failure",
        Level = LogLevel.Error,
        Message = "access.audit.flush.failure phase={Phase} exceptionType={ExceptionType}")]
    private static partial void LogFlushFailure(ILogger logger, string phase, string exceptionType);
}
