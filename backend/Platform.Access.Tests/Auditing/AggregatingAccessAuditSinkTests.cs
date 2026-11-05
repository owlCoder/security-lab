using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class AggregatingAccessAuditSinkTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private ManualTimeProvider _clock = null!;
    private RecordingAuditSink _inner = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualTimeProvider(Start);
        _inner = new RecordingAuditSink();
    }

    private AggregatingAccessAuditSink Sink(int maxKeys = 1024, IAccessAuditSink? inner = null) =>
        new(inner ?? _inner, new AccessAuditOptions { DenyAggregationWindow = Window, MaxAggregationKeys = maxKeys }, _clock);

    private AccessAuditRecord Denied(
        string actor = "user-1",
        string action = "lab.read",
        string reason = "no_policy",
        string correlationId = "corr-1",
        string target = "-") =>
        new(_clock.GetUtcNow(), AccessAuditRecord.DeniedEventType, actor, action, target, "deny",
            correlationId, reason, null, "v0");

    private AccessAuditRecord PrivilegedAllowed() =>
        new(_clock.GetUtcNow(), AccessAuditRecord.PrivilegedAllowedEventType, "admin-1", "account.status.change", "-",
            "allow", "corr-2", null, "identity.account.status.change.admin-only", "v1");

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FirstDeny_IsWrittenImmediately()
    {
        await Sink().WriteAsync(Denied());

        Assert.That(_inner.Records, Has.Count.EqualTo(1));
        Assert.That(_inner.Records[0].Count, Is.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_RepeatedDenies_AggregatedWithCount()
    {
        var sink = Sink();
        for (var i = 0; i < 5; i++)
        {
            await sink.WriteAsync(Denied());
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.That(_inner.Records, Has.Count.EqualTo(1), "repeats inside the window are held back");

        _clock.Advance(Window);
        await sink.WriteAsync(Denied(actor: "someone-else"));

        Assert.That(_inner.Records, Has.Count.EqualTo(3));
        var summary = _inner.Records.Single(r => r.Actor == "user-1" && r.Count > 1);
        Assert.That(summary.Count, Is.EqualTo(4));
        Assert.That(summary.EventType, Is.EqualTo(AccessAuditRecord.DeniedEventType));
        Assert.That(summary.Action, Is.EqualTo("lab.read"));
        Assert.That(summary.DenialReason, Is.EqualTo("no_policy"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_SummaryTime_IsTimeOfLastHeldBackDeny()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        _clock.Advance(TimeSpan.FromSeconds(3));
        await sink.WriteAsync(Denied());
        var lastRepeat = _clock.GetUtcNow();
        _clock.Advance(Window);
        await sink.WriteAsync(Denied(actor: "x"));

        var summary = _inner.Records.Single(r => r.Actor == "user-1" && r.Time == lastRepeat);
        Assert.That(summary.Count, Is.EqualTo(1));
    }

    [TestCase("actor")]
    [TestCase("action")]
    [TestCase("reason")]
    [TestCase("target")]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_DifferentActorActionReasonOrTarget_NotMerged(string differs)
    {
        var sink = Sink();
        await sink.WriteAsync(Denied(target: "lab:1"));

        await sink.WriteAsync(differs switch
        {
            "actor" => Denied(actor: "user-2", target: "lab:1"),
            "action" => Denied(action: "lab.write", target: "lab:1"),
            "reason" => Denied(reason: "unknown_action", target: "lab:1"),
            _ => Denied(target: "lab:2"),
        });

        Assert.That(_inner.Records, Has.Count.EqualTo(2));
        Assert.That(_inner.Records.Select(r => r.Count), Has.All.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_ProbingManyTargets_EveryTargetIsAuditedOnce()
    {
        var sink = Sink();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 10; i++)
            {
                await sink.WriteAsync(Denied(target: $"attempt:{i}", correlationId: $"corr-{round}-{i}"));
            }
        }

        Assert.That(_inner.Records.Select(r => r.Target), Is.EquivalentTo(Enumerable.Range(0, 10).Select(i => $"attempt:{i}")),
            "no target is hidden behind another one");

        _clock.Advance(Window);
        await sink.FlushExpiredAsync();

        var summaries = _inner.Records.Where(r => r.Count == 1 && r.CorrelationId.StartsWith("corr-1-")).ToList();
        Assert.That(summaries.Select(r => r.Target), Is.EquivalentTo(Enumerable.Range(0, 10).Select(i => $"attempt:{i}")),
            "each target keeps its own held-back repeat");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_SameTarget_StillAggregated()
    {
        var sink = Sink();

        for (var i = 0; i < 5; i++)
        {
            await sink.WriteAsync(Denied(target: "lab:1"));
        }

        Assert.That(_inner.Records, Has.Count.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_DifferentCorrelationId_StillAggregated()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied(correlationId: "corr-1"));
        await sink.WriteAsync(Denied(correlationId: "corr-2"));

        Assert.That(_inner.Records, Has.Count.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_WindowExpired_NewRecord()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        _clock.Advance(Window + TimeSpan.FromSeconds(1));

        await sink.WriteAsync(Denied());

        Assert.That(_inner.Records, Has.Count.EqualTo(2));
        Assert.That(_inner.Records.Select(r => r.Count), Has.All.EqualTo(1), "nothing was held back, so no summary");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_WindowEndsExactlyAtLimit()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());

        _clock.Advance(Window - TimeSpan.FromMilliseconds(1));
        await sink.WriteAsync(Denied());
        Assert.That(_inner.Records, Has.Count.EqualTo(1), "just inside the window");

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await sink.WriteAsync(Denied());
        Assert.That(_inner.Records, Has.Count.EqualTo(3), "summary of the held-back repeat plus a new first record");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_NewWindowForSameKey_ReportsOldSummaryAndStartsAgain()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        _clock.Advance(Window);

        await sink.WriteAsync(Denied());

        var records = _inner.Records;
        Assert.That(records.Select(r => r.Count), Is.EqualTo(new[] { 1, 2, 1 }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PrivilegedRecords_NeverAggregated()
    {
        var sink = Sink();

        for (var i = 0; i < 3; i++)
        {
            await sink.WriteAsync(PrivilegedAllowed());
        }

        Assert.That(_inner.Records, Has.Count.EqualTo(3));
        Assert.That(_inner.Records.Select(r => r.EventType), Has.All.EqualTo(AccessAuditRecord.PrivilegedAllowedEventType));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PrivilegedRecord_DoesNotFlushOrDisturbDenyWindows()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(PrivilegedAllowed());

        Assert.That(_inner.Records, Has.Count.EqualTo(2));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_KeyLimit_NothingIsLost_ExcessIsFoldedIntoOverflowCount()
    {
        var sink = Sink(maxKeys: 2);

        for (var i = 0; i < 5; i++)
        {
            await sink.WriteAsync(Denied(actor: $"user-{i}"));
        }

        Assert.That(_inner.Records, Has.Count.EqualTo(3), "two tracked keys plus the first record of the overflow window");

        await sink.FlushAllAsync();

        Assert.That(_inner.Records.Sum(r => r.Count), Is.EqualTo(5), "every denial is accounted for");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_KeyTableFull_FloodOfDistinctActors_ProducesBoundedRecords()
    {
        var sink = Sink(maxKeys: 2);

        for (var i = 0; i < 500; i++)
        {
            await sink.WriteAsync(Denied(actor: $"user-{i}"));
        }

        Assert.That(_inner.Records, Has.Count.EqualTo(3), "the other 497 denials are only counted");

        await sink.FlushAllAsync();

        Assert.That(_inner.Records, Has.Count.EqualTo(4), "plus one overflow summary");
        Assert.That(_inner.Records.Sum(r => r.Count), Is.EqualTo(500));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_OverflowSummary_HidesIndividualActorsButKeepsReasonAndCount()
    {
        var sink = Sink(maxKeys: 1);

        foreach (var actor in new[] { "a", "b", "c", "d" })
        {
            await sink.WriteAsync(Denied(actor: actor));
        }

        await sink.FlushAllAsync();

        var summary = _inner.Records.Single(r => r.Count == 2);
        Assert.That(summary.Actor, Is.EqualTo(AccessAuditRecord.NoValue), "a summary of many actors names none of them");
        Assert.That(summary.Action, Is.EqualTo(AccessAuditRecord.NoValue));
        Assert.That(summary.Target, Is.EqualTo(AccessAuditRecord.NoValue));
        Assert.That(summary.CorrelationId, Is.EqualTo(AccessAuditRecord.NoValue));
        Assert.That(summary.DenialReason, Is.EqualTo("no_policy"));
        Assert.That(summary.EventType, Is.EqualTo(AccessAuditRecord.DeniedEventType));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Overflow_IsCountedPerDenialReason()
    {
        var sink = Sink(maxKeys: 2);
        await sink.WriteAsync(Denied(actor: "a1"));
        await sink.WriteAsync(Denied(actor: "a2"));

        foreach (var actor in new[] { "b", "c", "c2" })
        {
            await sink.WriteAsync(Denied(actor: actor, reason: "no_policy"));
        }

        foreach (var actor in new[] { "d", "e", "e2" })
        {
            await sink.WriteAsync(Denied(actor: actor, reason: "unknown_action"));
        }

        await sink.FlushAllAsync();

        var summaries = _inner.Records.Where(r => r.Count == 2).ToList();
        Assert.That(summaries.Select(r => r.DenialReason), Is.EquivalentTo(new[] { "no_policy", "unknown_action" }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_KeyTableFull_TrackedKeysStillAggregateWithTheirOwnActor()
    {
        var sink = Sink(maxKeys: 1);
        await sink.WriteAsync(Denied(actor: "a"));
        await sink.WriteAsync(Denied(actor: "b"));
        await sink.WriteAsync(Denied(actor: "c"));
        for (var i = 0; i < 3; i++)
        {
            await sink.WriteAsync(Denied(actor: "a"));
        }

        await sink.FlushAllAsync();

        Assert.That(_inner.Records.Single(r => r.Actor == "a" && r.Count == 3).Count, Is.EqualTo(3), "own summary for the tracked key");
        Assert.That(_inner.Records.Single(r => r.Actor == AccessAuditRecord.NoValue).Count, Is.EqualTo(1), "overflow summary for c");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_OverflowWindow_ExpiresAndTheTableIsUsableAgain()
    {
        var sink = Sink(maxKeys: 1);
        foreach (var actor in new[] { "a", "b", "c" })
        {
            await sink.WriteAsync(Denied(actor: actor));
        }

        _clock.Advance(Window);
        await sink.FlushExpiredAsync();
        await sink.WriteAsync(Denied(actor: "d"));

        Assert.That(_inner.Records.Select(r => r.Actor), Is.EqualTo(new[] { "a", "b", AccessAuditRecord.NoValue, "d" }),
            "after expiry d is tracked and written like a first denial");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_OverflowTableAlsoFull_RecordsAreWritten_NotDropped()
    {
        var sink = Sink(maxKeys: 1);

        await sink.WriteAsync(Denied(actor: "a"));
        await sink.WriteAsync(Denied(actor: "b"));
        await sink.WriteAsync(Denied(actor: "c", reason: "unknown_action"));
        await sink.WriteAsync(Denied(actor: "c", reason: "unknown_action"));

        Assert.That(_inner.Records, Has.Count.EqualTo(4), "memory stays bounded and no record is dropped");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_OverflowFirstWriteFails_HeldRepeatsAreNotLost()
    {
        var gated = new GatedFailOnCallSink(gatedCall: 2);
        var sink = Sink(maxKeys: 1, inner: gated);
        await sink.WriteAsync(Denied(actor: "a"));
        var first = sink.WriteAsync(Denied(actor: "b")).AsTask();
        await sink.WriteAsync(Denied(actor: "c"));
        gated.Release();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await first);

        await sink.FlushAllAsync();

        Assert.That(gated.Inner.Records.Sum(r => r.Count), Is.EqualTo(2), "a plus the repeat counted while the overflow write was in flight");
        Assert.That(gated.Inner.Records.Last().Actor, Is.EqualTo(AccessAuditRecord.NoValue));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_ParallelFlood_WithFullKeyTable_EveryDenyAccountedFor()
    {
        var sink = Sink(maxKeys: 5);

        await Task.WhenAll(Enumerable.Range(0, 300).Select(i =>
            Task.Run(async () => await sink.WriteAsync(Denied(actor: $"user-{i}")))));
        await sink.FlushAllAsync();

        Assert.That(_inner.Records.Sum(r => r.Count), Is.EqualTo(300));
        Assert.That(_inner.Records.Count, Is.LessThan(20), "bounded number of records, not one per denial");
    }

    private sealed class GatedFailOnCallSink(int gatedCall) : IAccessAuditSink
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public RecordingAuditSink Inner { get; } = new();

        public void Release() => _gate.SetResult();

        public async ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == gatedCall)
            {
                await _gate.Task;
                throw new InvalidOperationException("sink failed");
            }

            await Inner.WriteAsync(record, cancellationToken);
        }
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_KeyLimit_ExpiredKeysAreReleased()
    {
        var sink = Sink(maxKeys: 2);
        await sink.WriteAsync(Denied(actor: "a"));
        await sink.WriteAsync(Denied(actor: "b"));
        _clock.Advance(Window);

        await sink.WriteAsync(Denied(actor: "c"));
        await sink.WriteAsync(Denied(actor: "c"));

        Assert.That(_inner.Records.Count(r => r.Actor == "c"), Is.EqualTo(1), "key c is tracked again after old keys expired");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_InnerFails_FirstDenyIsNotSwallowedAsRepeat()
    {
        var failing = new FailingThenRecordingSink(failures: 1);
        var sink = Sink(inner: failing);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await sink.WriteAsync(Denied()));
        await sink.WriteAsync(Denied());

        Assert.That(failing.Inner.Records, Has.Count.EqualTo(1), "a failed first write must not turn later denials into silent repeats");
    }

    private sealed class FailOnCallSink(int failingCall) : IAccessAuditSink
    {
        private int _calls;

        public RecordingAuditSink Inner { get; } = new();

        public ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) == failingCall
                ? throw new InvalidOperationException("sink failed")
                : Inner.WriteAsync(record, cancellationToken);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_SummaryWriteFails_CurrentDenyIsStillWritten()
    {
        var failing = new FailOnCallSink(failingCall: 2);
        var sink = Sink(inner: failing);
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        _clock.Advance(Window);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await sink.WriteAsync(Denied()));

        Assert.That(failing.Inner.Records, Has.Count.EqualTo(2), "the new first record was written despite the failed summary");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Token_IsPassedToInnerSink()
    {
        using var cts = new CancellationTokenSource();

        await Sink().WriteAsync(Denied(), cts.Token);

        Assert.That(_inner.LastToken, Is.EqualTo(cts.Token));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_NullRecord_IsRejected()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () => await Sink().WriteAsync(null!));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_ParallelDenies_NoCounterLost()
    {
        var sink = Sink();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(async () => await sink.WriteAsync(Denied()))));

        Assert.That(_inner.Records, Has.Count.EqualTo(1), "exactly one first record");

        _clock.Advance(Window);
        await sink.WriteAsync(Denied(actor: "flush"));

        var total = _inner.Records.Where(r => r.Actor == "user-1").Sum(r => r.Count);
        Assert.That(total, Is.EqualTo(100));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FlushExpired_WritesSummaryWithoutAnotherDeny()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        _clock.Advance(Window);

        await sink.FlushExpiredAsync();

        Assert.That(_inner.Records.Select(r => r.Count), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FlushExpired_LeavesOpenWindowsAlone()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        _clock.Advance(Window - TimeSpan.FromMilliseconds(1));

        await sink.FlushExpiredAsync();

        Assert.That(_inner.Records, Has.Count.EqualTo(1), "window is still open, count stays held back");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FlushAll_WritesHeldCountsOfOpenWindows_OnlyOnce()
    {
        var sink = Sink();
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());

        await sink.FlushAllAsync();
        await sink.FlushAllAsync();

        Assert.That(_inner.Records.Select(r => r.Count), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_SummaryWriteFails_HeldCountIsRetriedOnNextFlush()
    {
        var failing = new FailOnCallSink(failingCall: 2);
        var sink = Sink(inner: failing);
        await sink.WriteAsync(Denied());
        await sink.WriteAsync(Denied());
        _clock.Advance(Window);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await sink.FlushExpiredAsync());
        await sink.FlushExpiredAsync();

        Assert.That(failing.Inner.Records.Sum(r => r.Count), Is.EqualTo(2), "the held-back repeat was not lost");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_FirstWriteFails_RepeatsHeldMeanwhileAreNotLost()
    {
        var gated = new GatedFailingSink();
        var sink = Sink(inner: gated);
        var first = sink.WriteAsync(Denied()).AsTask();
        await sink.WriteAsync(Denied());
        gated.Release();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await first);

        await sink.FlushAllAsync();

        Assert.That(gated.Inner.Records.Sum(r => r.Count), Is.EqualTo(1), "the repeat counted while the first write was in flight");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PersistentSinkFailure_RetryBacklogIsBounded()
    {
        var toggle = new ToggleFailingSink();
        var sink = Sink(maxKeys: 1, inner: toggle);

        foreach (var actor in new[] { "a", "b" })
        {
            await sink.WriteAsync(Denied(actor: actor));
            await sink.WriteAsync(Denied(actor: actor));
            await sink.WriteAsync(Denied(actor: actor));
            _clock.Advance(Window);
            toggle.Fail = true;
            Assert.ThrowsAsync<InvalidOperationException>(async () => await sink.FlushExpiredAsync());
            toggle.Fail = false;
        }

        await sink.FlushExpiredAsync();

        var summaries = toggle.Inner.Records.Where(r => r.Count == 2).ToList();
        Assert.That(summaries, Has.Count.EqualTo(1), "backlog is capped at MaxAggregationKeys (1), the oldest summary survives");
        Assert.That(summaries[0].Actor, Is.EqualTo("a"));
    }

    private sealed class GatedFailingSink : IAccessAuditSink
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public RecordingAuditSink Inner { get; } = new();

        public void Release() => _gate.SetResult();

        public async ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await _gate.Task;
                throw new InvalidOperationException("sink failed");
            }

            await Inner.WriteAsync(record, cancellationToken);
        }
    }

    private sealed class ToggleFailingSink : IAccessAuditSink
    {
        public volatile bool Fail;

        public RecordingAuditSink Inner { get; } = new();

        public ValueTask WriteAsync(AccessAuditRecord record, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("sink failed") : Inner.WriteAsync(record, cancellationToken);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_ParallelMixedKeys_EveryDenyAccountedFor()
    {
        var sink = Sink();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(async () => await sink.WriteAsync(Denied(actor: $"user-{i % 10}")))));

        _clock.Advance(Window);
        await sink.WriteAsync(Denied(actor: "flush"));

        var total = _inner.Records.Where(r => r.Actor != "flush").Sum(r => r.Count);
        Assert.That(total, Is.EqualTo(200));
    }
}
