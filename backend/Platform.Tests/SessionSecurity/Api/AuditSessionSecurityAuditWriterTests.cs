using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.SessionSecurity.Audit;
using Platform.Audit.Contracts;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class AuditSessionSecurityAuditWriterTests
{
    private const string CorrelationId = "corr-1";
    private const string SyntheticToken = "c3ludGhldGljLXNlc3Npb24tdG9rZW4tdmFsdWU";

    // The hash the core stores for the synthetic token (D2: hex of SHA-256 over UTF-8).
    private static readonly string SyntheticTokenHash = new CryptoSessionTokenGenerator().ComputeHash(SyntheticToken);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    // Negative: nothing is written when the entry cannot be mapped.

    [Test]
    public async Task TryWriteAsync_WhenEntryIsNull_ReturnsFalseWithoutWriting()
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(null!);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(trusted.Writes, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("user-a")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("{aaaaaaaa-0000-4000-8000-000000000001}")]
    public async Task TryWriteAsync_WhenSubjectIsNotAUserId_ReturnsFalseWithoutWriting(string? subjectId)
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(Entry() with { SubjectId = subjectId });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(trusted.Writes, Is.Empty);
        });
    }

    [TestCase("auth.login")]
    [TestCase("session.unknown")]
    [TestCase("")]
    public async Task TryWriteAsync_WhenEventTypeIsNotSessionRotated_ReturnsFalseWithoutWriting(string eventType)
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(Entry() with { EventType = eventType });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(trusted.Writes, Is.Empty);
        });
    }

    [TestCase(SessionRotationCodes.InvalidRequest)]
    [TestCase("Unknown")]
    public async Task TryWriteAsync_WhenOutcomeIsNotMapped_ReturnsFalseWithoutWriting(string outcome)
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(Entry() with { Outcome = outcome });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(trusted.Writes, Is.Empty);
        });
    }

    [Test]
    public async Task TryWriteAsync_WhenTriggerIsUndefined_ReturnsFalseWithoutWriting()
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(Entry() with { Trigger = (RotationTrigger)99 });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(trusted.Writes, Is.Empty);
        });
    }

    // Negative: a failure of the Audit module is reported as false (AUD-10, fail-closed).

    [Test]
    public async Task TryWriteAsync_WhenAuditRejectsDraft_ReturnsFalse()
    {
        var trusted = new RecordingTrustedAuditWriter(AuditWriteResult.Rejected(AuditRejectionCode.UnknownEventType));

        var written = await CreateWriter(trusted).TryWriteAsync(Entry());

        Assert.That(written, Is.False);
    }

    [Test]
    public async Task TryWriteAsync_WhenAuditIsUnavailable_ReturnsFalse()
    {
        var trusted = new RecordingTrustedAuditWriter(AuditWriteResult.Unavailable());

        var written = await CreateWriter(trusted).TryWriteAsync(Entry());

        Assert.That(written, Is.False);
    }

    [Test]
    public async Task TryWriteAsync_WhenAuditThrows_ReturnsFalse()
    {
        var trusted = new RecordingTrustedAuditWriter(exception: new InvalidOperationException("storage failed"));

        var written = await CreateWriter(trusted).TryWriteAsync(Entry());

        Assert.That(written, Is.False);
    }

    [Test]
    public void TryWriteAsync_WhenAuditIsCancelled_PropagatesCancellation()
    {
        var trusted = new RecordingTrustedAuditWriter(exception: new OperationCanceledException());

        Assert.That(
            async () => await CreateWriter(trusted).TryWriteAsync(Entry()),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void TryWriteAsync_WhenTokenIsCancelled_PropagatesCancellationWithoutWriting()
    {
        var trusted = new RecordingTrustedAuditWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await CreateWriter(trusted).TryWriteAsync(Entry(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(trusted.Writes, Is.Empty);
    }

    // Positive: the draft and the context follow the session.rotated schema.

    [Test]
    public async Task TryWriteAsync_WhenRotated_WritesSuccessDraftWithTriggerOnly()
    {
        var trusted = new RecordingTrustedAuditWriter();

        var written = await CreateWriter(trusted).TryWriteAsync(Entry());

        var (draft, _) = trusted.Writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.SessionRotated));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.CourseId, Is.Null);
            Assert.That(draft.EventId, Is.Null);
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, UserId.ToString("D"))));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["trigger"] = "login" }));
        });
    }

    [Test]
    public async Task TryWriteAsync_WhenRotationIsRefused_WritesDeniedDraftWithReasonCode()
    {
        var trusted = new RecordingTrustedAuditWriter();
        var entry = Entry() with
        {
            Trigger = RotationTrigger.MfaCompleted,
            Outcome = SessionRotationCodes.SessionRevoked,
        };

        var written = await CreateWriter(trusted).TryWriteAsync(entry);

        var (draft, _) = trusted.Writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["trigger"] = "mfa_completed",
                ["reasonCode"] = "session_revoked",
            }));
        });
    }

    [Test]
    public async Task TryWriteAsync_WhenWritten_UsesUserActorAndCorrelationOfEntry()
    {
        var trusted = new RecordingTrustedAuditWriter();

        await CreateWriter(trusted).TryWriteAsync(Entry());

        var (_, context) = trusted.Writes.Single();
        Assert.That(context, Is.EqualTo(new AuditContext(AuditActor.ForUser(UserId), CorrelationId)));
    }

    [Test]
    public async Task TryWriteAsync_WhenWritten_DraftAndContextContainNoSessionSecret()
    {
        var trusted = new RecordingTrustedAuditWriter();

        await CreateWriter(trusted).TryWriteAsync(Entry());

        var (draft, context) = trusted.Writes.Single();
        var serialized = JsonSerializer.Serialize(draft) + JsonSerializer.Serialize(context);
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Not.Contain(SyntheticToken));
            Assert.That(serialized, Does.Not.Contain(SyntheticTokenHash));
            Assert.That(serialized, Does.Not.Contain("token").IgnoreCase);
            Assert.That(serialized, Does.Not.Contain("chain").IgnoreCase);
        });
    }

    private static AuditSessionSecurityAuditWriter CreateWriter(RecordingTrustedAuditWriter trusted) =>
        new(trusted, NullLogger<AuditSessionSecurityAuditWriter>.Instance);

    private static SessionSecurityAuditEntry Entry() => new(
        SessionSecurityAuditEvents.SessionRotated,
        UserId.ToString("D"),
        RotationTrigger.Login,
        SessionRotationCodes.Rotated,
        Now,
        CorrelationId);
}
