using System.Text.Json;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
public sealed class AuditWriterTests
{
    private static readonly DateTimeOffset FixedUtcTime =
        new(2026, 11, 4, 12, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task WriteAsync_WhenDraftIsValid_WritesTrustedImmutableEventWithServerUtcTime()
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock);
        var source = new Dictionary<string, string>
        {
            ["oldStatus"] = "active",
            ["newStatus"] = "blocked"
        };

        var result = await writer.WriteAsync(StatusChange(details: source));
        source["newStatus"] = "archived";

        var written = store.Events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(AuditWriteResult.Recorded(written.Id)));
            Assert.That(written.OccurredAt, Is.EqualTo(FixedUtcTime));
            Assert.That(written.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(written.ActorId, Is.EqualTo(UserId));
            Assert.That(written.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(written.Details["newStatus"], Is.EqualTo("blocked"));
            Assert.That(clock.GetUtcNowCallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task TrustedWriteAsync_WhenAnonymousContextIsValid_RecordsWithoutActorId()
    {
        var store = new RecordingStore();
        var writer = CreateWriter(store);
        var draft = new AuditEventDraft(
            AuditEventTypes.AuthLoginThrottled,
            AuditOutcome.Denied);

        var result = await ((ITrustedAuditWriter)writer).WriteAsync(draft, AnonymousContext);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Recorded));
            Assert.That(store.Events.Single().ActorKind, Is.EqualTo(AuditActorKind.Anonymous));
            Assert.That(store.Events.Single().ActorId, Is.Null);
        }
    }

    [Test]
    public async Task WriteAsync_WhenContextIsUnavailable_RejectsBeforeClockAndStore()
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock, new StubContextProvider(null));

        var result = await writer.WriteAsync(StatusChange());

        AssertRejectedWithoutSideEffects(
            result, AuditRejectionCode.InvalidContext, store, clock);
    }

    [Test]
    public async Task WriteAsync_WhenEventOrOutcomeIsNotInCatalog_RejectsBeforeSideEffects()
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock);
        var draft = new AuditEventDraft("auth.forged", AuditOutcome.Success);

        var result = await writer.WriteAsync(draft);

        AssertRejectedWithoutSideEffects(
            result, AuditRejectionCode.UnknownEventType, store, clock);
    }

    [TestCase("comment", "forged-actor-secret", AuditRejectionCode.UnknownDetailKey)]
    [TestCase("accessToken", "must-not-leak", AuditRejectionCode.ForbiddenDetailKey)]
    public async Task WriteAsync_WhenDetailIsNotAllowed_RejectsWithoutWritingOrLeaking(
        string key,
        string value,
        AuditRejectionCode expectedCode)
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock);

        var result = await writer.WriteAsync(StatusChange(details: Details((key, value))));

        AssertRejectedWithoutSideEffects(result, expectedCode, store, clock);
        Assert.That(result.ToString(), Does.Not.Contain(value));
    }

    [TestCase(20, AuditRejectionCode.UnknownDetailKey)]
    [TestCase(21, AuditRejectionCode.DetailsTooLarge)]
    public async Task WriteAsync_EnforcesTwentyDetailKeySizeGate(
        int keyCount,
        AuditRejectionCode expectedCode)
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock);
        var details = Enumerable.Range(0, keyCount)
            .ToDictionary(i => $"key{i}", _ => "x", StringComparer.Ordinal);

        var result = await writer.WriteAsync(StatusChange(details: details));

        // At 20 the size gate passes and the catalog then rejects the unknown key.
        // At 21 it stops immediately with DetailsTooLarge.
        AssertRejectedWithoutSideEffects(result, expectedCode, store, clock);
    }

    [TestCase(2048, AuditRejectionCode.UnknownDetailKey)]
    [TestCase(2049, AuditRejectionCode.DetailsTooLarge)]
    public async Task WriteAsync_EnforcesExactUtf8JsonSizeGate(
        int serializedByteCount,
        AuditRejectionCode expectedCode)
    {
        var store = new RecordingStore();
        var clock = new CountingTimeProvider(FixedUtcTime);
        var writer = CreateWriter(store, clock);
        var details = DetailsWithSerializedSize(serializedByteCount);
        Assert.That(JsonSerializer.SerializeToUtf8Bytes(details), Has.Length.EqualTo(serializedByteCount));

        var result = await writer.WriteAsync(StatusChange(details: details));

        // At 2048 the byte gate passes and catalog validation continues. At 2049
        // validation stops at the byte gate before inspecting any rejected value.
        AssertRejectedWithoutSideEffects(result, expectedCode, store, clock);
    }

    [Test]
    public async Task WriteAsync_WhenEventIdRetryHasSameContent_IsIdempotent()
    {
        var id = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var store = new RecordingStore();
        var writer = CreateWriter(store);
        var draft = StatusChange() with { EventId = id };

        var first = await writer.WriteAsync(draft);
        var retry = await writer.WriteAsync(draft);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(AuditWriteResult.Recorded(id)));
            Assert.That(retry, Is.EqualTo(AuditWriteResult.Recorded(id)));
            Assert.That(store.Events, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task WriteAsync_WhenEventIdRetryHasDifferentContent_RejectsConflict()
    {
        var id = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var store = new RecordingStore();
        var writer = CreateWriter(store);
        await writer.WriteAsync(StatusChange() with { EventId = id });
        var changed = StatusChange(
            details: Details(("oldStatus", "active"), ("newStatus", "archived"))) with
        {
            EventId = id
        };

        var result = await writer.WriteAsync(changed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Rejected));
            Assert.That(result.RejectionCode, Is.EqualTo(AuditRejectionCode.EventIdConflict));
            Assert.That(store.Events, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task WriteAsync_WhenStoreFails_ReturnsUnavailableWithoutLeakingFailure()
    {
        var store = new RecordingStore { ExceptionToThrow = new("database-password") };
        var writer = CreateWriter(store);

        var result = await writer.WriteAsync(StatusChange());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Unavailable));
            Assert.That(result.EventId, Is.Null);
            Assert.That(result.ToString(), Does.Not.Contain("database-password"));
            Assert.That(store.Events, Is.Empty);
        }
    }

    [Test]
    public void WriteAsync_WhenCallerHasCancelled_PropagatesCancellationWithoutWriting()
    {
        var store = new RecordingStore();
        var writer = CreateWriter(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await writer.WriteAsync(StatusChange(), cancellation.Token));
        Assert.That(store.Events, Is.Empty);
    }

    private static AuditWriter CreateWriter(
        RecordingStore store,
        CountingTimeProvider? clock = null,
        IAuditContextProvider? contextProvider = null) =>
        new(
            new AuditDraftValidator(),
            contextProvider ?? new StubContextProvider(UserContext),
            store,
            clock ?? new CountingTimeProvider(FixedUtcTime));

    private static Dictionary<string, string> DetailsWithSerializedSize(int byteCount)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal) { ["comment"] = string.Empty };
        var emptySize = JsonSerializer.SerializeToUtf8Bytes(details).Length;
        details["comment"] = new string('x', byteCount - emptySize);
        return details;
    }

    private static void AssertRejectedWithoutSideEffects(
        AuditWriteResult result,
        AuditRejectionCode expectedCode,
        RecordingStore store,
        CountingTimeProvider clock)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Rejected));
            Assert.That(result.RejectionCode, Is.EqualTo(expectedCode));
            Assert.That(result.EventId, Is.Null);
            Assert.That(store.Events, Is.Empty);
            Assert.That(clock.GetUtcNowCallCount, Is.Zero);
        }
    }

    private sealed class StubContextProvider(AuditContext? context) : IAuditContextProvider
    {
        public AuditContext? GetCurrentContext() => context;
    }

    private sealed class RecordingStore : IAuditEventStore
    {
        public List<AuditEvent> Events { get; } = [];
        public Exception? ExceptionToThrow { get; init; }

        public Task<AuditAppendStatus> AppendAsync(
            AuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            var existing = Events.SingleOrDefault(item => item.Id == auditEvent.Id);
            if (existing is null)
            {
                Events.Add(auditEvent);
                return Task.FromResult(AuditAppendStatus.Appended);
            }

            return Task.FromResult(existing.HasSameContentAs(auditEvent)
                ? AuditAppendStatus.AlreadyPresent
                : AuditAppendStatus.Conflict);
        }

        public Task<AuditEvent?> FindAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Events.SingleOrDefault(item => item.Id == id));
        }
    }

    private sealed class CountingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return utcNow;
        }
    }
}
