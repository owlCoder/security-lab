using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Audit.Contracts;

namespace Platform.Tests.RevisionAuthorization;

[TestFixture]
public sealed class RevisionAccessAuditWriterTests
{
    private static AccessRequest CreateRequest() =>
        new(
            new SubjectRef(Guid.NewGuid().ToString(), AccountState.Active),
            RevisionActions.Approve,
            new ResourceRef("revision", Guid.NewGuid().ToString()),
            "test-correlation");

    [Test]
    public async Task DeniedAction_WritesAuditEvent()
    {
        var audit = new FakeAuditWriter(AuditWriteResult.Recorded(Guid.NewGuid()));
        var writer = new RevisionAccessAuditWriter(
            audit,
            new FakeContextProvider());

        var request = CreateRequest();
        var decision = AccessDecision.Deny(
            AccessDenialReason.PolicyError,
            request.CorrelationId,
            "v1");

        await writer.WriteAsync(request, decision);

        Assert.That(audit.Calls, Is.EqualTo(1));
        Assert.That(audit.LastDraft?.EventType,
            Is.EqualTo(AuditEventTypes.AccessDenied));
        Assert.That(audit.LastDraft?.Outcome,
            Is.EqualTo(AuditOutcome.Denied));
        Assert.That(audit.LastContext?.CorrelationId,
            Is.EqualTo("test-correlation"));
    }

    [Test]
    public void RejectedAuditWrite_ThrowsException()
    {
        var audit = new FakeAuditWriter(
            AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext));

        var writer = new RevisionAccessAuditWriter(
            audit,
            new FakeContextProvider());

        var request = CreateRequest();
        var decision = AccessDecision.Deny(
            AccessDenialReason.PolicyError,
            request.CorrelationId);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.WriteAsync(request, decision));
    }

    [Test]
    public void UnavailableAuditWrite_ThrowsException()
    {
        var audit = new FakeAuditWriter(AuditWriteResult.Unavailable());

        var writer = new RevisionAccessAuditWriter(
            audit,
            new FakeContextProvider());

        var request = CreateRequest();
        var decision = AccessDecision.Deny(
            AccessDenialReason.PolicyError,
            request.CorrelationId);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.WriteAsync(request, decision));
    }

    [Test]
    public async Task AllowedAction_DoesNotWriteDeniedEvent()
    {
        var audit = new FakeAuditWriter(AuditWriteResult.Recorded(Guid.NewGuid()));

        var writer = new RevisionAccessAuditWriter(
            audit,
            new FakeContextProvider());

        var request = CreateRequest();
        var decision = AccessDecision.Allow(
            "rev05.revision-approval",
            request.CorrelationId,
            "v1");

        await writer.WriteAsync(request, decision);

        Assert.That(audit.Calls, Is.Zero);
    }

    private sealed class FakeAuditWriter(AuditWriteResult result)
        : ITrustedAuditWriter
    {
        public int Calls { get; private set; }
        public AuditEventDraft? LastDraft { get; private set; }
        public AuditContext? LastContext { get; private set; }

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDraft = draft;
            LastContext = context;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeContextProvider : IRevisionAuditContextProvider
    {
        public AuditContext GetContext() =>
            new(AuditActor.ForUser(Guid.NewGuid()), "test-correlation");
    }
}
