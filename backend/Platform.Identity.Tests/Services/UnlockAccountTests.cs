using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// IDN-05 administrator unlock / AUD-01 / AUD-03. Mock callbacks model commit/disposal and validate
/// every audit draft against the catalog. Nothing is persisted by these unit tests.
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

    // State belongs to this test harness; every production dependency is a Moq mock.
    private sealed class UnlockTestState
    {
        public Dictionary<Guid, UserAccount> Accounts { get; } = new();
        public Dictionary<Guid, DateTimeOffset?> LockoutEnds { get; } = new();
        public Dictionary<Guid, int> FailedLogins { get; } = new();
        public List<(AuditEventDraft Draft, AuditContext Context)> Audit { get; } = new();
        public List<(AuditEventDraft Draft, AuditContext Context)> PendingAudit { get; } = new();
        public int Commits { get; private set; }
        public bool FailOnClear { get; set; }
        public Mock<IAccountStatusStore> Store { get; } = new(MockBehavior.Strict);
        public Mock<IAccountStatusUnitOfWork> Work { get; } = new(MockBehavior.Strict);
        public Mock<DbTransaction> Transaction { get; } = new();

        public UnlockTestState()
        {
            var pendingClears = new HashSet<Guid>();
            Store.Setup(x => x.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Work.Object);
            Work.SetupGet(x => x.Transaction).Returns(Transaction.Object);
            Work.Setup(x => x.FindForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Accounts.GetValueOrDefault(id));
            Work.Setup(x => x.ClearLockoutAsync(It.IsAny<UserAccount>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserAccount account, DateTimeOffset now, CancellationToken _) =>
                {
                    if (FailOnClear) throw new Npgsql.NpgsqlException("simulated database failure");
                    if (LockoutEnds.GetValueOrDefault(account.Id) is not { } end || end <= now)
                        return (DateTimeOffset?)null;
                    pendingClears.Add(account.Id);
                    return end;
                });
            Work.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Callback(() =>
            {
                foreach (var id in pendingClears)
                {
                    LockoutEnds[id] = null;
                    FailedLogins[id] = 0;
                }
                pendingClears.Clear();
                Audit.AddRange(PendingAudit);
                PendingAudit.Clear();
                Commits++;
            }).Returns(Task.CompletedTask);
            Work.Setup(x => x.DisposeAsync()).Callback(() =>
            {
                pendingClears.Clear();
                PendingAudit.Clear();
            }).Returns(ValueTask.CompletedTask);
        }
    }

    private sealed class AuditSetup
    {
        public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());
        public Mock<ITransactionalAuditWriter> Writer { get; } = new(MockBehavior.Strict);

        public AuditSetup(UnlockTestState state)
        {
            Writer.Setup(x => x.WriteAsync(It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(),
                    state.Transaction.Object, It.IsAny<CancellationToken>()))
                .Returns((AuditEventDraft draft, AuditContext context, DbTransaction _, CancellationToken _) =>
                {
                    var rejection = new AuditDraftValidator().Validate(draft, context);
                    if (rejection != AuditRejectionCode.None)
                        return ValueTask.FromResult(AuditWriteResult.Rejected(rejection));
                    if (Result.IsRecorded) state.PendingAudit.Add((draft, context));
                    return ValueTask.FromResult(Result);
                });
        }
    }

    private static CallerIdentity Admin => new(true, AdminId, AuthenticationLevel.None, new Guid());
    private static CallerIdentity Student => new(true, StudentId, AuthenticationLevel.None, new Guid());

    /// <summary>The student is locked until <see cref="StudentLockoutEnd"/>; a test may change that afterwards.</summary>
    private static (UnlockAccount Sut, UnlockTestState Store, AuditSetup Audit) Build(
        CallerIdentity? caller,
        string? correlationId = "corr-1")
    {
        var store = new UnlockTestState();
        store.Accounts[StudentId] = new UserAccount(StudentId, StudentEmail);
        store.Accounts[AdminId] = new UserAccount(AdminId, "admin@example.com", role: AccountRole.Administrator);
        store.LockoutEnds[StudentId] = StudentLockoutEnd;
        store.FailedLogins[StudentId] = StudentFailures;
        // The administrator is locked too, so a self-unlock would have something to clear.
        store.LockoutEnds[AdminId] = Now.AddMinutes(10);
        store.FailedLogins[AdminId] = 0;

        var audit = new AuditSetup(store);
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(x => x.Current).Returns(caller);
        var access = new Mock<IAccessDecision>();
        access.Setup(x => x.CanUnlockAccount(It.IsAny<CallerIdentity>())).Returns(caller?.UserId == AdminId);
        var correlation = new Mock<ICorrelationContext>();
        correlation.SetupGet(x => x.CorrelationId).Returns(correlationId);
        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(Now);
        var sut = new UnlockAccount(identity.Object, access.Object, store.Store.Object,
            audit.Writer.Object, correlation.Object, clock.Object, NullLogger<UnlockAccount>.Instance);

        return (sut, store, audit);
    }

    [Test]
    public async Task Admin_UnlocksLockedAccount_LockoutCleared_CommittedOnce_AuditRecorded()
    {
        // Arrange
        var (sut, store, _) = Build(Admin);

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
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
            ["lockoutEnd"] = StudentLockoutEndInAudit,
            ["lockoutKind"] = "password"
        }));
        Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(AdminId)));
        Assert.That(context.CorrelationId, Is.EqualTo("corr-1"));
    }

    // The audit mock runs the real catalog validator, so each value here is also proven to be a valid UtcTimestamp.
    [TestCase(0L, "2026-11-23T10:15:00Z")]
    [TestCase(5_000_000L, "2026-11-23T10:15:00.5Z")]
    [TestCase(1_234_560L, "2026-11-23T10:15:00.123456Z")]   // PostgreSQL keeps microseconds
    [TestCase(1_234_567L, "2026-11-23T10:15:00.1234567Z")]
    public async Task LockoutEnd_IsRecordedInUtc_WithoutLosingItsFraction(long extraTicks, string expected)
    {
        // Arrange
        var (sut, store, _) = Build(Admin);
        store.LockoutEnds[StudentId] = StudentLockoutEnd.AddTicks(extraTicks);

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Success));
        Assert.That(store.Audit.Single().Draft.Details!["lockoutEnd"], Is.EqualTo(expected));
    }

    [Test]
    public async Task Anonymous_IsUnauthenticated_NothingChanges_DenialIsAuditedWithoutActor()
    {
        // Arrange
        var (sut, store, _) = Build(caller: null);

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
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
        // Arrange
        var (sut, store, _) = Build(new CallerIdentity(false, AdminId, AuthenticationLevel.None, new Guid()));

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unauthenticated));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
    }

    [Test]
    public async Task Student_IsForbidden_NothingChanges_DenialIsAudited()
    {
        // Arrange
        var (sut, store, _) = Build(Student);
        store.LockoutEnds[AdminId] = Now.AddMinutes(5);

        // Act
        var outcome = await sut.ExecuteAsync(AdminId);

        // Assert
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
        // Arrange
        var (sut, store, _) = Build(Student);

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Forbidden));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
    }

    [Test]
    public async Task Admin_CannotUnlockOwnAccount()
    {
        // Arrange
        var (sut, store, _) = Build(Admin);

        // Act
        var outcome = await sut.ExecuteAsync(AdminId);

        // Assert
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
        // Arrange
        var (sut, store, _) = Build(Admin);
        var unknown = Guid.NewGuid();

        // Act
        var outcome = await sut.ExecuteAsync(unknown);

        // Assert
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
        // Arrange
        DateTimeOffset? lockoutEnd = lockoutEndSecondsFromNow is { } seconds ? Now.AddSeconds(seconds) : null;
        var (sut, store, _) = Build(Admin);
        store.LockoutEnds[StudentId] = lockoutEnd;

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
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
        // Arrange
        var (sut, store, audit) = Build(Admin);
        audit.Result = rejected
            ? AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext)
            : AuditWriteResult.Unavailable();

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unavailable));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
        Assert.That(store.Commits, Is.Zero);
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task DatabaseFailure_ReturnsUnavailable_AndStoresNothing()
    {
        // Arrange
        var (sut, store, _) = Build(Admin);
        store.FailOnClear = true;

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Unavailable));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
        Assert.That(store.FailedLogins[StudentId], Is.EqualTo(StudentFailures));
        Assert.That(store.Audit, Is.Empty);
    }

    [Test]
    public async Task RefusalStaysRefused_WhenAuditIsUnavailable()
    {
        // Arrange
        var (sut, store, audit) = Build(Student);
        audit.Result = AuditWriteResult.Unavailable();

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Forbidden));
        Assert.That(store.LockoutEnds[StudentId], Is.EqualTo(StudentLockoutEnd));
    }

    [TestCase(null)]
    [TestCase("has space")]
    [TestCase("0HN:00000001")]
    public async Task WithoutValidCorrelationId_AValidOneIsGenerated(string? correlationId)
    {
        // Arrange
        var (sut, store, _) = Build(Admin, correlationId: correlationId);

        // Act
        var outcome = await sut.ExecuteAsync(StudentId);

        // Assert
        Assert.That(outcome, Is.EqualTo(UnlockAccountOutcome.Success));
        Assert.That(store.Audit.Single().Context.CorrelationId, Does.Match("^[A-Za-z0-9._-]{1,64}$"));
    }

    [Test]
    public async Task AuditRecords_NeverContainTheEmail()
    {
        // Arrange
        var (sut, store, _) = Build(Admin);

        // Act
        await sut.ExecuteAsync(StudentId);   // success
        await sut.ExecuteAsync(StudentId);   // now not locked: refusal

        // Assert
        Assert.That(store.Audit, Has.Count.EqualTo(2));
        Assert.That(store.Audit[0].Draft.Details!.Keys, Is.EquivalentTo(new[] { "lockoutEnd", "lockoutKind" }));
        foreach (var (draft, _) in store.Audit)
        {
            Assert.That(draft.Details!.Values.Any(value => value.Contains('@')), Is.False, "no e-mail in the details");
            Assert.That(draft.Resource!.Id, Is.EqualTo(StudentId.ToString("D")));
        }
    }
}
