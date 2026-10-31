using Platform.Identity.Domain;
using Platform.Api.Services.Identity;
using Platform.Api.Domain.Authentication;

namespace Platform.Tests.Identity;

[TestFixture]
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

    private sealed class RecordingRevoker : ISessionManager
    {
        public List<Guid> Revoked { get; } = new();
        public int RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
        {
            Revoked.Add(userId);
            int count = Revoked.Count;
            return count;
        }
        public bool RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
        {
            Revoked.Add(ownerUserId);
            return true;
        }

        public IReadOnlyList<SessionInfo> ListForUser(Guid userId, CancellationToken ct = default) => Array.Empty<SessionInfo>();
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent e) => Events.Add(e);
    }

    private static CallerIdentity Admin => new(true, AdminId, AuthenticationLevel.None, new Guid());

    private static (ChangeAccountStatus Sut, InMemoryAccountRepository Repo, RecordingRevoker Revoker, RecordingAudit Audit)
        Build(CallerIdentity? caller, AccountStatus studentStatus = AccountStatus.Active)
    {
        var repo = new InMemoryAccountRepository();
        repo.Save(new UserAccount(StudentId, "student@example.com", studentStatus));
        repo.Save(new UserAccount(AdminId, "admin@example.com"));

        var revoker = new RecordingRevoker();
        var audit = new RecordingAudit();
        var sut = new ChangeAccountStatus(
            new FakeIdentity(caller),
            new AdminOnlyAccessDecision(new FakeAdmins(AdminId)),
            repo, revoker, audit, TimeProvider.System);

        return (sut, repo, revoker, audit);
    }

    [Test]
    public void Admin_CanBlockAccount_StatusIsSaved_SessionsRevoked_AuditWritten()
    {
        var (sut, repo, revoker, audit) = Build(Admin);

        var result = sut.Execute(StudentId, "BLOCKED", "test", "corr-1");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(result.NewStatus, Is.EqualTo(AccountStatus.Blocked));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Blocked));
        Assert.That(revoker.Revoked, Is.EqualTo(new[] { StudentId }));

        var e = audit.Events.Single();
        Assert.That(e.ActorId, Is.EqualTo(AdminId));
        Assert.That(e.Target, Is.EqualTo(StudentId.ToString()));
        Assert.That(e.Outcome, Is.EqualTo("success"));
        Assert.That(e.CorrelationId, Is.EqualTo("corr-1"));
    }

    [Test]
    public void Admin_CanArchiveAccount_SessionsRevoked()
    {
        var (sut, repo, revoker, _) = Build(Admin);

        var result = sut.Execute(StudentId, "ARCHIVED", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Archived));
        Assert.That(revoker.Revoked, Is.EqualTo(new[] { StudentId }));
    }

    [Test]
    public void Admin_CanActivateBlockedAccount_SessionsNotRevoked()
    {
        var (sut, repo, revoker, _) = Build(Admin, AccountStatus.Blocked);

        var result = sut.Execute(StudentId, "active", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Success));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(revoker.Revoked, Is.Empty);
    }

    // --- negativni bezbednosni testovi ---

    [Test]
    public void NonAdmin_IsForbidden_StatusUnchanged_DenialAudited()
    {
        var (sut, repo, revoker, audit) = Build(new CallerIdentity(true, new Guid(), AuthenticationLevel.None, new Guid()));

        var result = sut.Execute(StudentId, "BLOCKED", "x", "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Forbidden));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(revoker.Revoked, Is.Empty);
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo("denied"));
    }

    [Test]
    public void NoCaller_IsUnauthenticated_StatusUnchanged()
    {
        var (sut, repo, _, _) = Build(null);

        var result = sut.Execute(StudentId, "BLOCKED", "x", "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Unauthenticated));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Active));
    }

    [Test]
    public void CallerMarkedNotAuthenticated_IsUnauthenticated_EvenIfIdIsAdmin()
    {
        var (sut, repo, _, _) = Build(new CallerIdentity(false, AdminId, AuthenticationLevel.None, new Guid()));

        var result = sut.Execute(StudentId, "BLOCKED", "x", "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Unauthenticated));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Active));
    }

    [Test]
    public void NonAdmin_CannotLearnWhetherAccountExists()
    {
        var (sut, _, _, _) = Build(new CallerIdentity(true, new Guid(), AuthenticationLevel.None, new Guid()));

        var result = sut.Execute(Guid.NewGuid(), "BLOCKED", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Forbidden));
    }

    [TestCase("SUPERUSER")]
    [TestCase("1")]
    [TestCase("99")]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase(null)]
    public void InvalidStatusValue_IsRejected_StatusUnchanged(string? raw)
    {
        var (sut, repo, revoker, _) = Build(Admin);

        var result = sut.Execute(StudentId, raw, null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.InvalidStatus));
        Assert.That(repo.Find(StudentId)!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(revoker.Revoked, Is.Empty);
    }

    [Test]
    public void UnknownAccount_ReturnsNotFound()
    {
        var (sut, _, _, _) = Build(Admin);

        var result = sut.Execute(Guid.NewGuid(), "BLOCKED", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.NotFound));
    }

    [Test]
    public void Admin_CannotChangeOwnStatus()
    {
        var (sut, repo, revoker, _) = Build(Admin);

        var result = sut.Execute(AdminId, "BLOCKED", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.SelfChangeNotAllowed));
        Assert.That(repo.Find(AdminId)!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(revoker.Revoked, Is.Empty);
    }

    [Test]
    public void SettingSameStatus_IsInvalidTransition_NoRevocation()
    {
        var (sut, _, revoker, _) = Build(Admin, AccountStatus.Blocked);

        var result = sut.Execute(StudentId, "BLOCKED", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.InvalidTransition));
        Assert.That(revoker.Revoked, Is.Empty);
    }

    [Test]
    public void Reason_CannotInjectFakeAuditLines()
    {
        var (sut, _, _, audit) = Build(Admin);

        sut.Execute(StudentId, "BLOCKED", "ok\nAUDIT actor=someone-else outcome=success", "corr");

        Assert.That(audit.Events.Single().Detail, Does.Not.Contain("\n"));
    }
}