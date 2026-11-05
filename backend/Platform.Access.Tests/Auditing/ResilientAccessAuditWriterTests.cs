using System.Diagnostics;
using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class ResilientAccessAuditWriterTests
{
    private const string Secret = "hunter2-super-secret";

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    private CollectingLogger<ResilientAccessAuditWriter> _logger = null!;

    [SetUp]
    public void SetUp() => _logger = new CollectingLogger<ResilientAccessAuditWriter>();

    private ResilientAccessAuditWriter Resilient(IAccessAuditWriter inner, TimeSpan? timeout = null) =>
        new(inner, new AccessAuditOptions { WriteTimeout = timeout ?? ShortTimeout }, TimeProvider.System, _logger);

    private sealed class FixedPolicy(bool permitted) : IAccessPolicy
    {
        public string Id => "test.policy";
        public string Version => "v1";

        public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permitted);
    }

    private static DefaultDenyAccessDecision Engine(IAccessAuditWriter writer, bool policyPermits = true)
    {
        var catalog = new InMemoryActionCatalog();
        var registry = new InMemoryAccessPolicyRegistry();
        registry.Register("evaluation.task.process", new FixedPolicy(policyPermits));
        return new DefaultDenyAccessDecision(
            catalog,
            registry,
            writer,
            new AccessDecisionOptions());
    }

    private static AccessRequest Request(string action = "evaluation.task.process", SubjectRef? subject = null) =>
        new(subject ?? new SubjectRef("user-1", AccountState.Active), action, null, "corr-1");

    private static IEnumerable<TestCaseData> FailingWriters()
    {
        yield return new TestCaseData(new SyncThrowingWriter(new InvalidOperationException(Secret))).SetName("SyncThrow");
        yield return new TestCaseData(new AsyncThrowingWriter(new InvalidOperationException(Secret))).SetName("AsyncThrow");
        yield return new TestCaseData(new SyncThrowingWriter(new OperationCanceledException())).SetName("SyncCancelled");
        yield return new TestCaseData(new AsyncThrowingWriter(new OutOfMemoryException())).SetName("AsyncOutOfMemory");
    }

    [TestCaseSource(nameof(FailingWriters))]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditWriterThrows_DenyStaysDeny(IAccessAuditWriter failing)
    {
        var engine = Engine(Resilient(failing));

        var decision = await engine.DecideAsync(Request("unknown.action"));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [TestCaseSource(nameof(FailingWriters))]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditWriterThrows_AllowStaysAllow(IAccessAuditWriter failing)
    {
        var engine = Engine(Resilient(failing));

        var decision = await engine.DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.PolicyId, Is.EqualTo("test.policy"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditWriterThrows_PolicyDenyStaysDeny()
    {
        var engine = Engine(Resilient(new AsyncThrowingWriter(new InvalidOperationException(Secret))), policyPermits: false);

        var decision = await engine.DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditWriterHangs_DecisionReturnedWithinTimeout()
    {
        var engine = Engine(Resilient(new HangingWriter()));

        var stopwatch = Stopwatch.StartNew();
        var decision = await engine.DecideAsync(Request("unknown.action"));
        stopwatch.Stop();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(_logger.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditTimeout_CancelsTokenGivenToWriter()
    {
        var observing = new TokenObservingWriter();

        await Resilient(observing).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr-1"));

        Assert.That(await Task.WhenAny(observing.Cancelled, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(observing.Cancelled));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_RequestAlreadyCancelled_AuditWriteStillHappens()
    {
        var recording = new TokenRecordingWriter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Resilient(recording).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr-1"), cts.Token);

        Assert.That(recording.Called, Is.True);
        Assert.That(recording.TokenWasCancelled, Is.False);
        Assert.That(_logger.Entries, Is.Empty);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AuditSucceeds_NoAlarm()
    {
        var recording = new TokenRecordingWriter();

        await Resilient(recording).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr-1"));

        Assert.That(_logger.Entries, Is.Empty);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    [Property("RequirementId", "AUD-03")]
    public async Task ACC05_AuditFailure_RaisesAlarmWithoutSecrets()
    {
        var failing = new AsyncThrowingWriter(new InvalidOperationException($"connection string password={Secret}"));

        await Resilient(failing).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr-1"));

        var entry = _logger.Entries.Single();
        Assert.That(entry.Level, Is.EqualTo(Microsoft.Extensions.Logging.LogLevel.Error));
        Assert.That(entry.EventId.Name, Is.EqualTo("access.audit.failure"));
        Assert.That(entry.Exception, Is.Null, "exception object (message, stack) must not reach the log");
        Assert.That(entry.Message, Does.Not.Contain(Secret));
        Assert.That(entry.Message, Does.Not.Contain("password"));
        Assert.That(entry.State.Values.Select(v => v?.ToString()), Has.None.Contains(Secret));
        Assert.That(entry.Message, Does.Contain("corr-1"));
        Assert.That(entry.Message, Does.Contain(nameof(InvalidOperationException)));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    [Property("RequirementId", "AUD-03")]
    public async Task ACC05_AuditFailure_AlarmCorrelationId_IsSanitized()
    {
        var request = new AccessRequest(new SubjectRef("u", AccountState.Active), "lab.read", null, "corr\r\nFORGED level=Critical");

        await Resilient(new SyncThrowingWriter(new InvalidOperationException()))
            .WriteAsync(request, AccessDecision.Deny(AccessDenialReason.UnknownAction, "x"));

        var message = _logger.Entries.Single().Message;
        Assert.That(message.Any(char.IsControl), Is.False);
        Assert.That(message, Does.Contain("correlationId=invalid"), "a hostile id is replaced, not cleaned into a plausible one");
        Assert.That(message, Does.Not.Contain("FORGED"));
        Assert.That(message, Does.Not.Contain("level=Critical"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AlarmLoggerThrows_StillNoException()
    {
        var broken = new ResilientAccessAuditWriter(
            new SyncThrowingWriter(new InvalidOperationException()),
            new AccessAuditOptions { WriteTimeout = ShortTimeout },
            TimeProvider.System,
            new ThrowingLogger<ResilientAccessAuditWriter>());

        var decision = await Engine(broken).DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_NullArguments_DoNotThrow()
    {
        var writer = Resilient(new SyncThrowingWriter(new InvalidOperationException()));

        Assert.DoesNotThrowAsync(async () => await writer.WriteAsync(null!, null!));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FaultAfterTimeout_IsObservedAndDoesNotEscape()
    {
        var late = new TaskCompletionSource();
        var inner = new LateFaultWriter(late.Task);

        await Resilient(inner).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr-1"));
        late.SetException(new InvalidOperationException(Secret));
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.That(_logger.Entries.Select(e => e.Message), Has.None.Contain(Secret));
    }

    private sealed class LateFaultWriter(Task gate) : IAccessAuditWriter
    {
        public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default) =>
            new(gate);
    }
}
