using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services;

/// <summary>
/// IDN-03 / UC-05 / ADM-01 / AUD-01 / AUD-03. The store fake behaves like a database transaction:
/// nothing is visible until commit, and leaving the scope without a commit discards everything.
/// The same behavior against PostgreSQL is covered by EfAccountStatusStoreTests.
/// </summary>
[TestFixture]
[Category("IDN-03")]
public class ChangeAccountStatusTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid StudentId = Guid.NewGuid();

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

    /// <summary>Accounts, sessions and Audit records of one fake database.</summary>
    private sealed class FakeStore : IAccountStatusStore
    {
        public Dictionary<Guid, UserAccount> Accounts { get; } = new();
        public Dictionary<Guid, int> ActiveSessions { get; } = new();
        public List<(AuditEventDraft Draft, AuditContext Context)> Audit { get; } = new();
        public int Commits { get; private set; }
        public bool FailOnApply { get; set; }

        public AccountStatus StatusOf(Guid id) => Accounts[id].Status;

        public Task<IAccountStatusUnitOfWork> BeginAsync(CancellationToken ct = default) =>
            Task.FromResult<IAccountStatusUnitOfWork>(new UnitOfWork(this));

        public sealed class UnitOfWork(FakeStore store) : IAccountStatusUnitOfWork
        {
            private readonly Dictionary<Guid, AccountStatus> _pendingStatus = new();
            private readonly HashSet<Guid> _pendingRevocations = new();
            public List<(AuditEventDraft, AuditContext)> PendingAudit { get; } = new();

            public DbTransaction Transaction => new FakeDbTransaction(this);

            public Task<UserAccount?> FindForUpdateAsync(Guid accountId, CancellationToken ct = default) =>
                Task.FromResult(store.Accounts.TryGetValue(accountId, out var stored)
                    ? new UserAccount(stored.Id, stored.Email, stored.Status, stored.Role)
                    : null);

            public Task<int> ApplyAsync(UserAccount account, AccountStatus newStatus, CancellationToken ct = default)
            {
                if (store.FailOnApply) throw new FakeDbException();

                _pendingStatus[account.Id] = newStatus;
                if (newStatus is not (AccountStatus.Blocked or AccountStatus.Archived))
                    return Task.FromResult(0);

                _pendingRevocations.Add(account.Id);
                return Task.FromResult(store.ActiveSessions.GetValueOrDefault(account.Id));
            }

            public Task CommitAsync(CancellationToken ct = default)
            {
                foreach (var (id, status) in _pendingStatus) store.Accounts[id].Status = status;
                foreach (var id in _pendingRevocations) store.ActiveSessions[id] = 0;
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
        public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());

        public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            // Like the real writer, the record joins the caller's transaction instead of committing alone.
            if (Result.IsRecorded)
                ((FakeDbTransaction)transaction).Work.PendingAudit.Add((draft, context));
            return ValueTask.FromResult(Result);
        }
    }

    private static CallerIdentity Admin => new(true, AdminId, AuthenticationLevel.None, new Guid());
    private static CallerIdentity Student => new(true, StudentId, AuthenticationLevel.None, new Guid());

    private static (ChangeAccountStatus Sut, FakeStore Store, FakeAudit Audit) Build(
        CallerIdentity? caller,
        AccountStatus studentStatus = AccountStatus.Active,
        string? correlationId = "corr-1")
    {
        var store = new FakeStore();
        store.Accounts[StudentId] = new UserAccount(StudentId, "student@example.com", studentStatus);
        store.Accounts[AdminId] = new UserAccount(AdminId, "admin@example.com", role: AccountRole.Administrator);
        store.ActiveSessions[StudentId] = 3;

        var audit = new FakeAudit();
        var sut = new ChangeAccountStatus(
            new FakeIdentity(caller),
            new AdminOnlyAccessDecision(new FakeAdmins(AdminId)),
            store, audit, new FakeCorrelation(correlationId),
            NullLogger<ChangeAccountStatus>.Instance);

        return (sut, store, audit);
    }

    [Test]
    public async Task Admin_BlocksAccount_StatusSaved_SessionsRevoked_AuditRecorded()
    {
        var (sut, store, _) = Build(Admin);

        var result = await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(result.NewStatus, Is.EqualTo(AccountStatus.Blocked));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Blocked));
        Assert.That(store.ActiveSessions[StudentId], Is.Zero);

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccountStatusChanged));
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, StudentId.ToString("D"))));
        Assert.That(draft.Details, Is.EqualTo(new Dictionary<string, string>
        {
            ["oldStatus"] = "active",
            ["newStatus"] = "blocked"
        }));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(AdminId)));
        Assert.That(context.CorrelationId, Is.EqualTo("corr-1"));
    }

    [Test]
    public async Task Admin_ArchivesAccount_RevokesSessions()
    {
        var (sut, store, _) = Build(Admin);

        var result = await sut.ExecuteAsync(StudentId, "archived");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Archived));
        Assert.That(store.ActiveSessions[StudentId], Is.Zero);
    }

    [Test]
    public async Task Admin_ActivatesBlockedAccount_DoesNotTouchSessions()
    {
        var (sut, store, _) = Build(Admin, AccountStatus.Blocked);

        var result = await sut.ExecuteAsync(StudentId, "Active");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AuditNotRecorded_RollsBackStatusAndSessions(bool rejected)
    {
        var (sut, store, audit) = Build(Admin);
        audit.Result = rejected
            ? AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext)
            : AuditWriteResult.Unavailable();

        var result = await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Unavailable));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task DatabaseFailure_ReturnsUnavailable_AndStoresNothing()
    {
        var (sut, store, _) = Build(Admin);
        store.FailOnApply = true;

        var result = await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Unavailable));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task Student_IsForbidden_NothingChanges_DenialIsAudited()
    {
        var (sut, store, _) = Build(Student);

        var result = await sut.ExecuteAsync(AdminId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Forbidden));
        Assert.That(store.StatusOf(AdminId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("not_administrator"));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(StudentId)));
    }

    [Test]
    public async Task Anonymous_IsUnauthenticated_NothingChanges_DenialIsAuditedWithoutActor()
    {
        var (sut, store, _) = Build(caller: null);

        var result = await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Unauthenticated));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));

        var (draft, context) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1")]
    [TestCase("99")]
    [TestCase("root")]
    [TestCase("blocked!")]
    public async Task InvalidStatusValue_IsRejected_NothingChanges(string? status)
    {
        var (sut, store, _) = Build(Admin);

        var result = await sut.ExecuteAsync(StudentId, status);

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.InvalidStatus));
        Assert.That(store.StatusOf(StudentId), Is.EqualTo(AccountStatus.Active));
        Assert.That(store.Audit.Single().Draft.Details!["reasonCode"], Is.EqualTo("invalid_status"));
    }

    [Test]
    public async Task UnknownAccount_ReturnsNotFound_AndIsAudited()
    {
        var (sut, store, _) = Build(Admin);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.NotFound));
        var (draft, _) = store.Audit.Single();
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
        Assert.That(draft.Details!["reasonCode"], Is.EqualTo("account_not_found"));
    }

    [Test]
    public async Task Admin_CannotChangeOwnStatus()
    {
        var (sut, store, _) = Build(Admin);

        var result = await sut.ExecuteAsync(AdminId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.SelfChangeNotAllowed));
        Assert.That(store.StatusOf(AdminId), Is.EqualTo(AccountStatus.Active));
    }

    [Test]
    public async Task SameStatus_IsInvalidTransition_SessionsStay()
    {
        var (sut, store, _) = Build(Admin, AccountStatus.Blocked);

        var result = await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.InvalidTransition));
        Assert.That(store.ActiveSessions[StudentId], Is.EqualTo(3));
        Assert.That(store.Audit.Single().Draft.Details!["reasonCode"], Is.EqualTo("invalid_transition"));
    }

    [Test]
    public async Task RefusalStaysRefused_WhenAuditIsUnavailable()
    {
        var (sut, store, audit) = Build(Student);
        audit.Result = AuditWriteResult.Unavailable();

        var result = await sut.ExecuteAsync(AdminId, "BLOCKED");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Forbidden));
        Assert.That(store.StatusOf(AdminId), Is.EqualTo(AccountStatus.Active));
    }

    [Test]
    public async Task AuditRecord_HasNoFreeTextOrIdentifiersOfSessions()
    {
        var (sut, store, _) = Build(Admin);

        await sut.ExecuteAsync(StudentId, "BLOCKED");

        var details = store.Audit.Single().Draft.Details!;
        Assert.That(details.Keys, Is.EquivalentTo(new[] { "oldStatus", "newStatus" }));
    }

    [TestCase(null)]
    [TestCase("has space")]
    [TestCase("0HN:00000001")]
    public async Task WithoutValidCorrelationId_AValidOneIsGenerated(string? correlationId)
    {
        var (sut, store, _) = Build(Admin, correlationId: correlationId);

        await sut.ExecuteAsync(StudentId, "BLOCKED");

        Assert.That(store.Audit.Single().Context.CorrelationId, Does.Match("^[A-Za-z0-9._-]{1,64}$"));
    }
}
