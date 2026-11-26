using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-08-09")]
public sealed class DecisionTraceAccessAuditWriterTests
{
    private const string SubjectId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const int PrivilegedAllowedEvent = 6103;
    private const int TraceEvent = 6104;

    private static readonly DecisionTrace Trace = new(
    [
        new TraceStep("subject.administrator", TraceResult.Passed),
        new TraceStep("grant.holder", TraceResult.Failed)
    ]);

    private sealed class RecordingLogger : ILogger<DecisionTraceAccessAuditWriter>
    {
        public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
    }

    private sealed class RecordingInner : IAccessAuditWriter
    {
        public List<(AccessDecision Decision, DecisionTrace? Trace, CancellationToken Token)> Writes { get; } = [];

        public ValueTask WriteAsync(
            AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
        {
            Writes.Add((decision, null, cancellationToken));
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(
            AccessRequest request, AccessDecision decision, DecisionTrace trace,
            CancellationToken cancellationToken = default)
        {
            Writes.Add((decision, trace, cancellationToken));
            return ValueTask.CompletedTask;
        }
    }

    private static (DecisionTraceAccessAuditWriter Writer, RecordingInner Inner, RecordingLogger Logger) Create()
    {
        var inner = new RecordingInner();
        var logger = new RecordingLogger();
        return (new DecisionTraceAccessAuditWriter(inner, new InMemoryActionCatalog(), new DecisionTraceFormatter(), logger), inner, logger);
    }

    private static AccessRequest Request(string action, string subjectId = SubjectId, string correlationId = "corr-1") =>
        new(new SubjectRef(subjectId, AccountState.Active), action, null, correlationId);

    private static AccessDecision Denied(string correlationId = "corr-1") =>
        AccessDecision.Deny(AccessDenialReason.NotPermitted, correlationId);

    private static AccessDecision Allowed(string correlationId = "corr-1") =>
        AccessDecision.Allow("elevated-grant.results.sensitive.read", correlationId, "v1");

    private static IAccessAuditWriter AsInterface(DecisionTraceAccessAuditWriter writer) => writer;

    [Test]
    public async Task DeniedDecisionWithTrace_LogsTheTraceForTheCorrelation()
    {
        var (writer, _, logger) = Create();

        await AsInterface(writer).WriteAsync(Request("results.sensitive.read"), Denied(), Trace);

        Assert.That(logger.Entries.Single(), Is.EqualTo((
            LogLevel.Information,
            TraceEvent,
            "access.decision.trace action=results.sensitive.read outcome=Deny correlationId=corr-1 trace=subject.administrator=passed,grant.holder=failed")));
    }

    [Test]
    public async Task DeniedDecisionWithoutTrace_LogsNothingExtra()
    {
        var (writer, inner, logger) = Create();

        await writer.WriteAsync(Request("results.sensitive.read"), Denied());

        Assert.That(logger.Entries, Is.Empty);
        Assert.That(inner.Writes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PrivilegedAllow_IsLoggedWithSubjectAndPolicy()
    {
        var (writer, _, logger) = Create();

        await AsInterface(writer).WriteAsync(Request("results.sensitive.read"), Allowed(), Trace);

        Assert.That(logger.Entries.Single(), Is.EqualTo((
            LogLevel.Information,
            PrivilegedAllowedEvent,
            $"access.privileged.allowed action=results.sensitive.read subjectId={SubjectId} policyId=elevated-grant.results.sensitive.read policyVersion=v1 correlationId=corr-1 trace=subject.administrator=passed,grant.holder=failed")));
    }

    [Test]
    public async Task PrivilegedAllowWithoutTrace_IsStillLogged()
    {
        var (writer, _, logger) = Create();

        await writer.WriteAsync(Request("results.sensitive.read"), Allowed());

        Assert.That(logger.Entries.Single().EventId, Is.EqualTo(PrivilegedAllowedEvent));
        Assert.That(logger.Entries.Single().Message, Does.EndWith("trace=none"));
    }

    [Test]
    public async Task NonPrivilegedAllow_IsNotLogged()
    {
        var (writer, _, logger) = Create();

        await AsInterface(writer).WriteAsync(Request(AccessActions.AttemptRead.Id), Allowed(), Trace);

        Assert.That(logger.Entries, Is.Empty);
    }

    [Test]
    public async Task AllowForActionOutsideTheCatalog_IsNotLoggedAsPrivileged()
    {
        var (writer, _, logger) = Create();

        await AsInterface(writer).WriteAsync(Request("unknown.action"), Allowed(), Trace);

        Assert.That(logger.Entries, Is.Empty);
    }

    [Test]
    public async Task UntrustedValues_AreNeverWrittenToTheLog()
    {
        var (writer, _, logger) = Create();
        var forged = new DecisionTrace([new TraceStep("grant.holder\r\nforged", TraceResult.Failed)]);

        await AsInterface(writer).WriteAsync(
            Request("results.sensitive.read\r\nforged", "subject\r\nforged", "corr\r\nforged"),
            Denied("corr\r\nforged"),
            forged);
        await AsInterface(writer).WriteAsync(
            Request("results.sensitive.read", "subject\r\nforged", "corr\r\nforged"),
            AccessDecision.Allow("Bad\r\nforged", "corr\r\nforged", "1.0\r\nforged"),
            forged);

        Assert.That(logger.Entries, Has.Count.EqualTo(2));
        Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contain("forged"));
        Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contain("\r"));
    }

    [Test]
    public async Task AnonymousPrivilegedAllow_IsLoggedWithoutSubject()
    {
        var (writer, _, logger) = Create();
        var request = new AccessRequest(null, "results.sensitive.read", null, "corr-1");

        await AsInterface(writer).WriteAsync(request, Allowed(), Trace);

        Assert.That(logger.Entries.Single().Message, Does.Contain("subjectId=anonymous"));
    }

    [Test]
    public async Task EveryDecision_IsForwardedToTheWrappedWriter()
    {
        var (writer, inner, _) = Create();
        using var source = new CancellationTokenSource();

        await AsInterface(writer).WriteAsync(Request("results.sensitive.read"), Denied(), Trace, source.Token);
        await writer.WriteAsync(Request("results.sensitive.read"), Allowed(), source.Token);

        Assert.That(inner.Writes, Has.Count.EqualTo(2));
        Assert.That(inner.Writes[0].Trace, Is.SameAs(Trace));
        Assert.That(inner.Writes[0].Token, Is.EqualTo(source.Token));
        Assert.That(inner.Writes[1].Token, Is.EqualTo(source.Token));
    }

    [Test]
    public void DecisionCarriedToTheCaller_HasNoTraceMember()
    {
        var memberTypes = typeof(AccessDecision)
            .GetProperties()
            .Select(property => property.PropertyType);

        Assert.That(memberTypes, Has.None.EqualTo(typeof(DecisionTrace)));
        Assert.That(memberTypes, Has.None.EqualTo(typeof(IReadOnlyList<TraceStep>)));
    }
}
