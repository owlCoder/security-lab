using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class AccessAuditFlushServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static AccessAuditRecord Denied() =>
        new(DateTimeOffset.UtcNow, AccessAuditRecord.DeniedEventType, "user-1", "lab.read", "-", "deny",
            "corr-1", "no_policy", null, "v0");

    private AccessAuditFlushService Service(
        IAccessAuditSink inner,
        TimeSpan window,
        TimeSpan interval,
        out AggregatingAccessAuditSink sink,
        ILogger<AccessAuditFlushService>? logger = null)
    {
        var options = new AccessAuditOptions { DenyAggregationWindow = window, FlushInterval = interval };
        sink = new AggregatingAccessAuditSink(inner, options, TimeProvider.System);
        return new AccessAuditFlushService(
            sink, options, TimeProvider.System, logger ?? NullLogger<AccessAuditFlushService>.Instance);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private static async Task DenyThreeTimes(AggregatingAccessAuditSink sink)
    {
        for (var i = 0; i < 3; i++)
        {
            await sink.WriteAsync(Denied());
        }
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Timer_WritesSummary_WithoutAnotherDeny()
    {
        var inner = new RecordingAuditSink();
        var service = Service(inner, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(20), out var sink);
        await DenyThreeTimes(sink);
        await service.StartAsync(CancellationToken.None);

        var written = await WaitUntil(() => inner.Records.Count == 2);
        await service.StopAsync(CancellationToken.None);

        Assert.That(written, Is.True, "summary appeared although no further denial arrived");
        Assert.That(inner.Records.Select(r => r.Count), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Timer_DoesNotCloseOpenWindow()
    {
        var inner = new RecordingAuditSink();
        var service = Service(inner, TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(10), out var sink);
        await DenyThreeTimes(sink);
        await service.StartAsync(CancellationToken.None);

        await Task.Delay(150);

        Assert.That(inner.Records, Has.Count.EqualTo(1), "window is still open, count stays held back");
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Shutdown_FlushesOpenWindows()
    {
        var inner = new RecordingAuditSink();
        var service = Service(inner, TimeSpan.FromHours(1), TimeSpan.FromHours(1), out var sink);
        await DenyThreeTimes(sink);
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.That(inner.Records.Select(r => r.Count), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Timer_SurvivesFailingSink_AndRetriesWhenItRecovers()
    {
        var toggle = new ToggleFailingSink();
        var service = Service(toggle, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(20), out var sink);
        await DenyThreeTimes(sink);
        toggle.Fail = true;
        await service.StartAsync(CancellationToken.None);

        var retried = await WaitUntil(() => toggle.Attempts >= 3);
        toggle.Fail = false;
        var recovered = await WaitUntil(() => toggle.Inner.Records.Any(r => r.Count == 2));
        await service.StopAsync(CancellationToken.None);

        Assert.That(retried, Is.True, "loop kept ticking after a failed flush");
        Assert.That(recovered, Is.True, "held-back count was written once the sink recovered");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Shutdown_WithFailingSink_DoesNotThrow_AndAlarmCarriesNoSecrets()
    {
        var logs = new CollectingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var toggle = new ToggleFailingSink();
        var service = Service(toggle, TimeSpan.FromHours(1), TimeSpan.FromHours(1), out var sink,
            factory.CreateLogger<AccessAuditFlushService>());
        await DenyThreeTimes(sink);
        toggle.Fail = true;
        await service.StartAsync(CancellationToken.None);

        Assert.DoesNotThrowAsync(async () => await service.StopAsync(CancellationToken.None));

        var alarm = logs.Entries.Single(e => e.EventId.Name == "access.audit.flush.failure");
        Assert.That(alarm.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(alarm.Message, Does.Contain("phase=shutdown"));
        Assert.That(alarm.Message, Does.Contain("exceptionType=InvalidOperationException"));
        Assert.That(alarm.Message, Does.Not.Contain("sink failed"), "exception message is never logged");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Stop_WithoutStart_DoesNotThrow()
    {
        var service = Service(new RecordingAuditSink(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), out _);

        Assert.DoesNotThrowAsync(async () => await service.StopAsync(CancellationToken.None));
    }

    private sealed class ToggleFailingSink : IAccessAuditSink
    {
        private int _attempts;

        public volatile bool Fail;

        public RecordingAuditSink Inner { get; } = new();

        public int Attempts => Volatile.Read(ref _attempts);

        public ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            return Fail ? throw new InvalidOperationException("sink failed") : Inner.WriteAsync(record, cancellationToken);
        }
    }
}
