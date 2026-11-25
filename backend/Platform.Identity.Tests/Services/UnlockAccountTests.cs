using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services;

/// <summary>
/// IDN-05 administrator unlock / AUD-01 / AUD-03. The store fake behaves like a database transaction, as in
/// <see cref="ChangeAccountStatusTests"/>: nothing is visible until commit, and leaving the scope without a commit
/// discards everything. The audit fake checks every draft against the real catalog, as the real writer does.
/// The same behavior against PostgreSQL is covered by AccountUnlockIntegrationTests.
/// </summary>
[TestFixture]
[Category("IDN-05")]
public class UnlockAccountTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    // 11:15 at +01:00 is 10:15 UTC; the Audit record must carry the UTC value.
    private static readonly DateTimeOffset StudentLockoutEnd = new(2026, 11, 23, 11, 15, 0, TimeSpan.FromHours(1));
    private const string StudentLockoutEndInAudit = "2026-11-23T10:15:00Z";
    private const string StudentEmail = "student@example.com";
    private const int StudentFailures = 4;

    private sealed class FakeIdentity(CallerIdentity? caller) : IIdentityContext
    {
        public CallerIdentity? Current => caller;
    }

    private sealed class FakeAdmins(params Guid[] ids) : IAdminDirectory
    {
        public bool IsAdministrator(Guid userId) => ids.Contains(userId);
    }

    private sealed class FakeCorrelation(string? id) : ICorrelationContext
    {
        public string? CorrelationId => id;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeDbException : DbException
    {
        public FakeDbException() : base("simulated database failure") { }
    }

    private sealed class FakeDbTransaction(FakeStore.UnitOfWork work) : DbTransaction
    {
        public FakeStore.UnitOfWork Work => work;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => null;
        public override void Commit() { }
        public override void Rollback() { }
    }

    /// <summary>Accounts, their lockout state and Audit records of one fake database.</summary>
    private sealed class FakeStore : IAccountStatusStore
    {
        public Dictionary<Guid, UserAccount> Accounts { get; } = new();
        public Dictionary<Guid, DateTimeOffset?> LockoutEnds { get; } = new();
        public Dictionary<Guid, int> FailedLogins { get; } = new();
        public List<(AuditEventDraft Draft, AuditContext Context)> Audit { get; } = new();
        public int Commits { get; private set; }
        public bool FailOnClear { get; set; }

        public Task<IAccountStatusUnitOfWork> BeginAsync(CancellationToken ct = default) =>
            Task.FromResult<IAccountStatusUnitOfWork>(new UnitOfWork(this));

        public sealed class UnitOfWork(FakeStore store) : IAccountStatusUnitOfWork
        {
            private readonly HashSet<Guid> _pendingClears = new();
            public List<(AuditEventDraft, AuditContext)> PendingAudit { get; } = new();

            public DbTransaction Transaction => new FakeDbTransaction(this);

            public Task<UserAccount?> FindForUpdateAsync(Guid accountId, CancellationToken ct = default) =>
                Task.FromResult(store.Accounts.TryGetValue(accountId, out var stored)
                    ? new UserAccount(stored.Id, stored.Email, stored.Status, stored.Role)
                    : null);

            // An unlock never changes the status or the sessions; calling this fails the test.
            public Task<int> ApplyAsync(UserAccount account, AccountStatus newStatus, CancellationToken ct = default) =>
                throw new NotSupportedException();

            // Same rule as EfAccountStatusStore: only a lockout that is still running is cleared.
            public Task<DateTimeOffset?> ClearLockoutAsync(UserAccount account, DateTimeOffset now, CancellationToken ct = default)
            {
                if (store.FailOnClear) throw new FakeDbException();

                if (store.LockoutEnds.GetValueOrDefault(account.Id) is not { } end || end <= now)
                    return Task.FromResult<DateTimeOffset?>(null);

                _pendingClears.Add(account.Id);
                return Task.FromResult<DateTimeOffset?>(end);
            }

            public Task CommitAsync(CancellationToken ct = default)
            {
                foreach (var id in _pendingClears)
                {
                    store.LockoutEnds[id] = null;
                    store.FailedLogins[id] = 0;
                }
                store.Audit.AddRange(PendingAudit);
                store.Commits++;
                return Task.CompletedTask;
            }

            // No commit, no change.
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAudit : ITransactionalAuditWriter
    {
        private readonly AuditDraftValidator _validator = new();

        public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());

        public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            // Like the real writer, a draft outside the catalog is rejected and never stored.
            var rejection = _validator.Validate(draft, context);
            if (rejection != AuditRejectionCode.None)
                return ValueTask.FromResult(AuditWriteResult.Rejected(rejection));

            // Like the real writer, the record joins the caller's transaction instead of committing alone.
            if (Result.IsRecorded)
                ((FakeDbTransaction)transaction).Work.PendingAudit.Add((draft, context));
            return ValueTask.FromResult(Result);
        }
    }

    private static CallerIdentity Admin => new(true, AdminId, AuthenticationLevel.None, new Guid());
    private static CallerIdentity Student => new(true, StudentId, AuthenticationLevel.None, new Guid());

    /// <summary>The student is locked until <see cref="StudentLockoutEnd"/>; a test may change that afterwards.</summary>
    private static (UnlockAccount Sut, FakeStore Store, FakeAudit Audit) Build(
        CallerIdentity? caller,
        string? correlationId = "corr-1")
    {
        var store = new FakeStore();
        store.Accounts[StudentId] = new UserAccount(StudentId, StudentEmail);
        store.Accounts[AdminId] = new UserAccount(AdminId, "admin@example.com", role: AccountRole.Administrator);
        store.LockoutEnds[StudentId] = StudentLockoutEnd;
        store.FailedLogins[StudentId] = StudentFailures;
        // The administrator is locked too, so a self-unlock would have something to clear.
        store.LockoutEnds[AdminId] = Now.AddMinutes(10);
        store.FailedLogins[AdminId] = 0;

        var audit = new FakeAudit();
        var sut = new UnlockAccount(
            new FakeIdentity(caller),
            new AdminOnlyAccessDecision(new FakeAdmins(AdminId)),
            store, audit, new FakeCorrelation(correlationId),
            new FixedClock(Now),
            NullLogger<UnlockAccount>.Instance);

        return (sut, store, audit);
    }

    [Test]
    public async Task Admin_UnlocksLockedAccount_LockoutCleared_CommittedOnce_AuditRecorded()
    {
        var (sut, store, _) = Build(Admin);

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Success));
        Assert.That(store.LockoutEnds[StudentId], Is.Null);
        Assert.That(store.FailedLogins[StudentId], Is.Zero);
        Assert.That(store.Commits, Is.EqualTo(1));
        Assert.That(store.Accounts[StudentId].Status, Is.EqualTo(AccountStatus.Active));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccountUnlocked));
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(draft.CourseId, Is.Null);
        Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, StudentId.ToString("D"))));
        Assert.That(draft.Details, Is.EqualTo(new Dictionary<string, string>
        {
            ["lockoutEnd"] = StudentLockoutEndInAudit
        }));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(AdminId)));
        Assert.That(context.CorrelationId, Is.EqualTo("corr-1"));
    }

    // The audit fake runs the real catalog validator, so each value here is also proven to be a valid UtcTimestamp.
    [TestCase(0L, "2026-11-23T10:15:00Z")]
    [TestCase(5_000_000L, "2026-11-23T10:15:00.5Z")]
    [TestCase(1_234_560L, "2026-11-23T10:15:00.123456Z")]   // PostgreSQL keeps microseconds
    [TestCase(1_234_567L, "2026-11-23T10:15:00.1234567Z")]
    public async Task LockoutEnd_IsRecordedInUtc_WithoutLosingItsFraction(long extraTicks, string expected)
    {
        var (sut, store, _) = Build(Admin);
        store.LockoutEnds[StudentId] = StudentLockoutEnd.AddTicks(extraTicks);

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Success));
        Assert.That(store.Audit.Single().Draft.Details!["lockoutEnd"], Is.EqualTo(expected));
    }

    [Test]
    public async Task Anonymous_IsUnauthenticated_NothingChanges_DenialIsAuditedWithoutActor()
    {
        var (sut, store, _) = Build(caller: null);

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unauthenticated));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccountUnlocked));
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("unauthenticated"));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    [Test]
    public async Task SessionWithoutFullAuthentication_IsUnauthenticated()
    {
        var (sut, store, _) = Build(new CallerIdentity(false, AdminId, AuthenticationLevel.None, new Guid()));

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unauthenticated));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
    }

    [Test]
    public async Task Student_IsForbidden_NothingChanges_DenialIsAudited()
    {
        var (sut, store, _) = Build(Student);
        store.LockoutEnds[AdminId] = Now.AddMinutes(5);

        var outcome = await sut.ExecuteAsync(AdminId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Forbidden));
        Assert.That(store.LockoutEnds[AdminId], Is.EqualTo(Now.AddMinutes(5)));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("not_administrator"));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(StudentId)));
    }

    [Test]
    public async Task Student_CannotUnlockOwnAccount_IsForbiddenFirst()
    {
        var (sut, store, _) = Build(Student);

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Forbidden));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
    }

    [Test]
    public async Task Admin_CannotUnlockOwnAccount()
    {
        var (sut, store, _) = Build(Admin);

        var outcome = await sut.ExecuteAsync(AdminId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.SelfChangeNotAllowed));
        Assert.That(store.LockoutEnds[AdminId], Is.EqualTo(Now.AddMinutes(10)));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("self_change"));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(AdminId)));
    }

    [Test]
    public async Task UnknownAccount_ReturnsNotFound_AndIsAudited()
    {
        var (sut, store, _) = Build(Admin);
        var unknown = Guid.NewGuid();

        var outcome = await sut.ExecuteAsync(unknown);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.NotFound));
        var (draft, _) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(draft.Resource!.Id, Is.EqualTo(unknown.ToString("D")));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("account_not_found"));
    }

    [TestCase(null, Description = "never locked")]
    [TestCase(-60, Description = "lockout already expired")]
    [TestCase(0, Description = "lockout ends exactly now")]
    public async Task AccountThatIsNotLocked_ReturnsNotLocked_NothingChanges(int? lockoutEndSecondsFromNow)
    {
        DateTimeOffset? lockoutEnd = lockoutEndSecondsFromNow is { } seconds ? Now.AddSeconds(seconds) : null;
        var (sut, store, _) = Build(Admin);
        store.LockoutEnds[StudentId] = lockoutEnd;

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.NotLocked));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(lockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures), "the failure counter is not reset");

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(draft.Details, Is.EqualTo(new Dictionary<string, string> { ["reasonCode"] = "not_locked" }));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(AdminId)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AuditNotRecorded_RollsBackTheUnlock(bool rejected)
    {
        var (sut, store, audit) = Build(Admin);
        audit.Result = rejected
            ? AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext)
            : AuditWriteResult.Unavailable();

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unavailable));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
        Assert.That(store.Commits, Is.Zero);
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task DatabaseFailure_ReturnsUnavailable_AndStoresNothing()
    {
        var (sut, store, _) = Build(Admin);
        store.FailOnClear = true;

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unavailable));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task RefusalStaysRefused_WhenAuditIsUnavailable()
    {
        var (sut, store, audit) = Build(Student);
        audit.Result = AuditWriteResult.Unavailable();

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Forbidden));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
    }

    [TestCase(null)]
    [TestCase("has space")]
    [TestCase("0HN:00000001")]
    public async Task WithoutValidCorrelationId_AValidOneIsGenerated(string? correlationId)
    {
        var (sut, store, _) = Build(Admin, correlationId: correlationId);

        var outcome = await sut.ExecuteAsync(StudentId);

        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Success));
        Assert.That(store.Audit.Single().Context.CorrelationId, Does.Match("^[A-Za-z0-9._-]{1,64}$"));
    }

    [Test]
    public async Task AuditRecords_NeverContainTheEmail()
    {
        var (sut, store, _) = Build(Admin);

        await sut.ExecuteAsync(StudentId);   // success
        await sut.ExecuteAsync(StudentId);   // now not locked: refusal

        Assert.That(store.Audit, Has.Count.EqualTo(2));
        Assert.That(store.Audit[0].Draft.Details!.Keys, Is.EquivalentTo(new[] { "lockoutEnd" }));
        foreach (var (draft, _) in store.Audit)
        {
            Assert.That(draft.Details!.Values.Any(value => value.Contains('@')), Is.False, "no e-mail in the details");
            Assert.That(draft.Resource!.Id, Is.EqualTo(StudentId.ToString("D")));
        }
    }
}
