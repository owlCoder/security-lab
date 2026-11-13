using Microsoft.Extensions.DependencyInjection;
using Platform.Audit.Composition;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests;

[TestFixture]
public sealed class MfaBackupCodeAuditServiceTests
{
    [Test]
    public Task Generated_RecordsSafeEvent()
    {
        return VerifyEventAsync(
            (service, userId) =>
                service.RecordGeneratedAsync(userId),
            AuditEventTypes.AuthMfaBackupCodesGenerated,
            AuditOutcome.Success);
    }

    [Test]
    public Task Redeemed_RecordsSafeEvent()
    {
        return VerifyEventAsync(
            (service, userId) =>
                service.RecordRedeemedAsync(userId),
            AuditEventTypes.AuthMfaBackupCodeRedeemed,
            AuditOutcome.Success);
    }

    [Test]
    public Task Rejected_RecordsSafeEvent()
    {
        return VerifyEventAsync(
            (service, userId) =>
                service.RecordRejectedAsync(userId),
            AuditEventTypes.AuthMfaBackupCodeRejected,
            AuditOutcome.Denied);
    }

    [Test]
    public Task Revoked_RecordsSafeEvent()
    {
        return VerifyEventAsync(
            (service, userId) =>
                service.RecordRevokedAsync(userId),
            AuditEventTypes.AuthMfaBackupCodesRevoked,
            AuditOutcome.Success);
    }

    [Test]
    public async Task Rejected_UnknownUser_DoesNotExposeAccount()
    {
        var writer = new CapturingAuditWriter();
        var service = new MfaBackupCodeAuditService(writer);

        var result = await service.RecordRejectedAsync(
            Guid.Empty);

        Assert.That(result.IsRecorded, Is.True);
        Assert.That(writer.Drafts, Has.Count.EqualTo(1));

        var draft = writer.Drafts[0];

        Assert.That(
            draft.EventType,
            Is.EqualTo(
                AuditEventTypes.AuthMfaBackupCodeRejected));

        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(draft.Resource, Is.Null);
        Assert.That(draft.Details, Is.Null);
    }

    [Test]
    public async Task InvalidUser_IsRejectedWithoutWriting()
    {
        var writer = new CapturingAuditWriter();
        var service = new MfaBackupCodeAuditService(writer);

        var generated = await service.RecordGeneratedAsync(
            Guid.Empty);

        var redeemed = await service.RecordRedeemedAsync(
            Guid.Empty);

        var revoked = await service.RecordRevokedAsync(
            Guid.Empty);

        Assert.That(generated.IsRecorded, Is.False);
        Assert.That(redeemed.IsRecorded, Is.False);
        Assert.That(revoked.IsRecorded, Is.False);

        Assert.That(
            generated.RejectionCode,
            Is.EqualTo(AuditRejectionCode.InvalidResource));

        Assert.That(writer.Drafts, Is.Empty);
    }

    [Test]
    public async Task WriterUnavailable_IsPropagated()
    {
        var writer = new CapturingAuditWriter
        {
            NextResult = AuditWriteResult.Unavailable()
        };

        var service = new MfaBackupCodeAuditService(writer);

        var result = await service.RecordGeneratedAsync(
            Guid.NewGuid());

        Assert.That(
            result.Status,
            Is.EqualTo(AuditWriteStatus.Unavailable));

        Assert.That(result.IsRecorded, Is.False);
    }

    [TestCase(AuditEventTypes.AuthMfaBackupCodesGenerated)]
    [TestCase(AuditEventTypes.AuthMfaBackupCodeRedeemed)]
    [TestCase(AuditEventTypes.AuthMfaBackupCodeRejected)]
    [TestCase(AuditEventTypes.AuthMfaBackupCodesRevoked)]
    public void EventType_IsRegisteredInCatalog(string eventType)
    {
        bool exists = AuditEventCatalog.TryGet(
            eventType, out var schema);

        Assert.That(exists, Is.True);
        Assert.That(schema, Is.Not.Null);
    }

    private static async Task VerifyEventAsync(
        Func<IMfaBackupCodeAuditService,
            Guid, ValueTask<AuditWriteResult>> operation,
        string expectedEventType,
        AuditOutcome expectedOutcome)
    {
        var writer = new CapturingAuditWriter();
        var service = new MfaBackupCodeAuditService(writer);

        Guid userId = Guid.NewGuid();

        var result = await operation(service, userId);

        Assert.That(result.IsRecorded, Is.True);
        Assert.That(writer.Drafts, Has.Count.EqualTo(1));

        var draft = writer.Drafts[0];

        Assert.Multiple(() =>
        {
            Assert.That(
                draft.EventType,
                Is.EqualTo(expectedEventType));

            Assert.That(
                draft.Outcome,
                Is.EqualTo(expectedOutcome));

            Assert.That(
                draft.Resource,
                Is.EqualTo(new AuditResourceRef(
                    AuditResourceTypes.Account,
                    userId.ToString("D"))));

            Assert.That(draft.Details, Is.Null);
            Assert.That(draft.CourseId, Is.Null);
            Assert.That(draft.EventId, Is.Null);
        });
    }

    private sealed class CapturingAuditWriter : IAuditWriter
    {
        public List<AuditEventDraft> Drafts { get; } = [];

        public AuditWriteResult NextResult { get; set; } =
            AuditWriteResult.Recorded(Guid.NewGuid());

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            CancellationToken cancellationToken = default)
        {
            Drafts.Add(draft);

            return ValueTask.FromResult(NextResult);
        }
    }
}
