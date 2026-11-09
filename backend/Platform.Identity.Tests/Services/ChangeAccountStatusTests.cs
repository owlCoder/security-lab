using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services;

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

        private readonly HashSet<(Guid UserId, Guid SessionHandle)> _activeSessions = new();

        public void AddSession(Guid userId, Guid sessionHandle) => _activeSessions.Add((userId, sessionHandle));

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default)
        {
            _activeSessions.RemoveWhere(session => session.UserId == userId);
            Revoked.Add(userId);
            int count = Revoked.Count;
            return Task.FromResult(count);
        }
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
        {
            if (!Guid.TryParse(token, out var handle))
                return Task.FromResult<ValidateSession?>(null);

            foreach (var session in _activeSessions)
            {
                if (session.SessionHandle == handle)
                    return Task.FromResult<ValidateSession?>(
                        new ValidateSession(session.SessionHandle, session.UserId, AuthenticationLevel.Active));
            }

            return Task.FromResult<ValidateSession?>(null);
        }

        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default)
        {
            Revoked.Add(ownerUserId);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<SessionInfo>)Array.Empty<SessionInfo>());
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

    // --- tests for checking sessionstate after account status change ---
    [Test]
    public async Task ReactivatingAccount_DoesNotRestorePreviouslyRevokedSessions()
    {
        var (sut, _, revoker, _) = Build(Admin);

        var sessionId = Guid.NewGuid();
        revoker.AddSession(StudentId, sessionId);

        // Sanity check: the session is valid before blocking
        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Not.Null);

        sut.Execute(StudentId, "BLOCKED", null, "corr-1");

        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Null);

        sut.Execute(StudentId, "ACTIVE", null, "corr-2");

        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Null);
    }

    [Test]
    public async Task BlockingAccount_InvalidatesAllSessionsOfThatUser()
    {
        var (sut, _, revoker, _) = Build(Admin);

        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        revoker.AddSession(StudentId, session1);
        revoker.AddSession(StudentId, session2);

        Assert.That(await revoker.ValidateAsync(session1.ToString()), Is.Not.Null);
        Assert.That(await revoker.ValidateAsync(session2.ToString()), Is.Not.Null);

        sut.Execute(StudentId, "BLOCKED", null, "corr");

        Assert.That(await revoker.ValidateAsync(session1.ToString()), Is.Null);
        Assert.That(await revoker.ValidateAsync(session2.ToString()), Is.Null);
    }

    [Test]
    public async Task ArchivingAccount_InvalidatesSessions()
    {
        var (sut, _, revoker, _) = Build(Admin);

        var sessionId = Guid.NewGuid();
        revoker.AddSession(StudentId, sessionId);

        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Not.Null);

        sut.Execute(StudentId, "ARCHIVED", null, "corr");

        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Null);
    }

    [Test]
    public async Task BlockingAccount_DoesNotAffectOtherUsersSessions()
    {
        var (sut, _, revoker, _) = Build(Admin);

        var studentSession = Guid.NewGuid();
        var adminSession = Guid.NewGuid();
        revoker.AddSession(StudentId, studentSession);
        revoker.AddSession(AdminId, adminSession);

        sut.Execute(StudentId, "BLOCKED", null, "corr");

        Assert.That(await revoker.ValidateAsync(studentSession.ToString()), Is.Null);

        var adminResult = await revoker.ValidateAsync(adminSession.ToString());
        Assert.That(adminResult, Is.Not.Null);
        Assert.That(adminResult!.UserId, Is.EqualTo(AdminId));
    }

    [Test]
    public async Task ActivatingBlockedAccount_DoesNotInvalidateExistingSessions()
    {
        var (sut, _, revoker, _) = Build(Admin, AccountStatus.Blocked);

        var sessionId = Guid.NewGuid();
        revoker.AddSession(StudentId, sessionId);

        sut.Execute(StudentId, "ACTIVE", null, "corr");

        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Not.Null);
    }

    [Test]
    public async Task NonAdmin_CannotBlock_SessionsRemainValid()
    {
        var (sut, _, revoker, _) = Build(new CallerIdentity(true, new Guid(), AuthenticationLevel.None, new Guid()));

        var sessionId = Guid.NewGuid();
        revoker.AddSession(StudentId, sessionId);

        var result = sut.Execute(StudentId, "BLOCKED", "x", "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.Forbidden));
        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Not.Null);
    }
    
    [Test]
    public async Task InvalidStatusValue_SessionsRemainValid()
    {
        var (sut, _, revoker, _) = Build(Admin);

        var sessionId = Guid.NewGuid();
        revoker.AddSession(StudentId, sessionId);

        var result = sut.Execute(StudentId, "SUPERUSER", null, "corr");

        Assert.That(result.Outcome, Is.EqualTo(ChangeStatusOutcome.InvalidStatus));
        Assert.That(await revoker.ValidateAsync(sessionId.ToString()), Is.Not.Null);
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