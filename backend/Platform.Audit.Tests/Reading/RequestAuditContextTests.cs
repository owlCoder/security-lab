using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Tests.Reading;

/// <summary>AUD-01/AUD-04: the ordinary IAuditWriter takes actor, time and correlation from the server.</summary>
[TestFixture]
[Property("RequirementId", "AUD-01")]
public sealed class RequestAuditContextTests
{
    private static readonly Guid User = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Test]
    public void Provider_CombinesActorAndServerCorrelation()
    {
        var provider = new RequestAuditContextProvider(new Actors(AuditActor.ForUser(User)), new Correlation("srv-1"));

        Assert.That(provider.GetCurrentContext(), Is.EqualTo(new AuditContext(AuditActor.ForUser(User), "srv-1")));
    }

    [Test]
    public void Provider_WithoutActor_ReturnsNoContext()
    {
        var provider = new RequestAuditContextProvider(new Actors(null), new Correlation("srv-1"));

        Assert.That(provider.GetCurrentContext(), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Provider_WithoutCorrelation_ReturnsNoContext(string? correlationId)
    {
        var provider = new RequestAuditContextProvider(new Actors(AuditActor.ForUser(User)), new Correlation(correlationId));

        Assert.That(provider.GetCurrentContext(), Is.Null);
    }

    [Test]
    public async Task Writer_RecordsSessionActor_ServerCorrelation_AndServerUtcTime()
    {
        var store = new Store();
        var writer = Writer(store, new Actors(AuditActor.ForUser(User)), new Correlation("srv-2"));

        var result = await writer.WriteAsync(new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success));

        var stored = store.Events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Recorded));
            Assert.That(stored.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(stored.ActorId, Is.EqualTo(User));
            Assert.That(stored.CorrelationId, Is.EqualTo("srv-2"));
            Assert.That(stored.OccurredAt, Is.EqualTo(Now));
        }
    }

    [Test]
    public async Task Writer_OutsideARequest_RejectsWithInvalidContext_AndStoresNothing()
    {
        var store = new Store();
        var writer = Writer(store, new Actors(null), new Correlation("srv-3"));

        var result = await writer.WriteAsync(new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext)));
            Assert.That(store.Events, Is.Empty);
        }
    }

    [Test]
    public async Task Writer_AnonymousCaller_CannotRecordAnEventThatRequiresAUser()
    {
        var store = new Store();
        var writer = Writer(store, new Actors(AuditActor.Anonymous), new Correlation("srv-4"));

        var result = await writer.WriteAsync(new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RejectionCode, Is.EqualTo(AuditRejectionCode.InvalidContext));
            Assert.That(store.Events, Is.Empty);
        }
    }

    [Test]
    public async Task Writer_SecretLikeDetail_IsRejected_AndNeverStored()
    {
        var store = new Store();
        var writer = Writer(store, new Actors(AuditActor.Anonymous), new Correlation("srv-5"));
        var draft = new AuditEventDraft(AuditEventTypes.AuthLogin, AuditOutcome.Failure,
            Details: new Dictionary<string, string> { ["password"] = "hunter2" });

        var result = await writer.WriteAsync(draft);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuditWriteStatus.Rejected));
            Assert.That(result.RejectionCode, Is.EqualTo(AuditRejectionCode.ForbiddenDetailKey));
            Assert.That(result.ToString(), Does.Not.Contain("hunter2"));
            Assert.That(store.Events, Is.Empty);
        }
    }

    private static AuditWriter Writer(Store store, IAuditActorSource actors, ICorrelationContext correlation) =>
        new(new AuditDraftValidator(), new RequestAuditContextProvider(actors, correlation), store, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Actors(AuditActor? actor) : IAuditActorSource
    {
        public AuditActor? GetCurrentActor() => actor;
    }

    private sealed class Correlation(string? id) : ICorrelationContext
    {
        public string? CorrelationId => id;
    }

    private sealed class Store : IAuditEventStore
    {
        public List<AuditEvent> Events { get; } = [];

        public Task<AuditAppendStatus> AppendAsync(AuditEvent record, CancellationToken cancellationToken = default)
        {
            Events.Add(record);
            return Task.FromResult(AuditAppendStatus.Appended);
        }

        public Task<AuditEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Events.FirstOrDefault(e => e.Id == id));
    }
}
