using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06: MfaService with strict Moq mocks of the stores, the unit of work, the clock and both Audit writers.</summary>
public abstract class MfaServiceTestBase
{
    protected const string Token = "session-token";
    protected const string NewToken = "token-of-the-new-active-session";
    protected static readonly Guid UserId = Guid.NewGuid();
    protected readonly DateTimeOffset _now = new(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);
    protected Mock<ISessionStore> _sessions = null!;
    protected Mock<IMfaCredentialsStore> _credentials = null!;
    protected Mock<IMfaUnitOfWork> _work = null!;
    protected Mock<ITrustedAuditWriter> _trustedAudit = null!;
    protected Mock<ITransactionalAuditWriter> _transactionalAudit = null!;
    protected List<AuditWrite> _auditWrites = null!;
    protected Mock<TimeProvider> _clock = null!;
    protected DbTransaction _transaction = null!;
    protected Session _newActiveSession = null!;
    protected ITotpSecretProtector _protector = null!;
    protected MfaService _service = null!;

    protected sealed record AuditWrite(AuditEventDraft Draft, AuditContext Context, DbTransaction? Transaction);

    [SetUp]
    public void SetUpService()
    {
        _sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        _credentials = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        _clock = new Mock<TimeProvider>(MockBehavior.Strict);
        _clock.Setup(clock => clock.GetUtcNow()).Returns(_now);
        _transaction = new Mock<DbTransaction>().Object;
        _newActiveSession = new Session(
            Guid.NewGuid(), UserId, [1, 2, 3], Guid.NewGuid(), AuthLevel.Active, _now, _now.AddHours(8));
        _work = new Mock<IMfaUnitOfWork>(MockBehavior.Strict);
        _work.SetupGet(work => work.Transaction).Returns(_transaction);
        _work.Setup(work => work.TryLockActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<AuthLevel>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _work.Setup(work => work.TryCreatePendingAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _work.Setup(work => work.TryConfirmEnrollmentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _work.Setup(work => work.TryConsumeTimeStepAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _work.Setup(work => work.TryReplaceWithActiveSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((NewToken, _newActiveSession));
        _work.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _work.Setup(work => work.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _credentials.Setup(store => store.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_work.Object);
        _auditWrites = [];
        _trustedAudit = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        _transactionalAudit = new Mock<ITransactionalAuditWriter>(MockBehavior.Strict);
        SetupAuditResults();
        _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        _service = CreateService(new MfaLockoutOptions());
    }

    protected MfaService CreateService(MfaLockoutOptions lockout) =>
        new(_credentials.Object, _sessions.Object,
            new IdentityAuditProducer(
                _trustedAudit.Object, _transactionalAudit.Object, NullLogger<IdentityAuditProducer>.Instance),
            _clock.Object, _protector, Options.Create(lockout), NullLogger<MfaService>.Instance);

    /// <summary>Both Audit writers record every write in <see cref="_auditWrites"/> and return these results.</summary>
    protected void SetupAuditResults(AuditWriteResult? trusted = null, AuditWriteResult? transactional = null)
    {
        _trustedAudit.Setup(writer => writer.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .Callback((AuditEventDraft draft, AuditContext context, CancellationToken _) =>
                _auditWrites.Add(new AuditWrite(draft, context, null)))
            .ReturnsAsync(trusted ?? AuditWriteResult.Recorded(Guid.NewGuid()));
        _transactionalAudit.Setup(writer => writer.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<DbTransaction>(),
                It.IsAny<CancellationToken>()))
            .Callback((AuditEventDraft draft, AuditContext context, DbTransaction transaction, CancellationToken _) =>
                _auditWrites.Add(new AuditWrite(draft, context, transaction)))
            .ReturnsAsync(transactional ?? AuditWriteResult.Recorded(Guid.NewGuid()));
    }

    protected void VerifyLockedSession(AuthLevel level) =>
        _work.Verify(work => work.TryLockActiveSessionAsync(
            UserId, _sessionId, level, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);

    protected void VerifyCreatePending(Times times) =>
        _work.Verify(work => work.TryCreatePendingAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), times);

    protected void VerifyConfirm(Times times) =>
        _work.Verify(work => work.TryConfirmEnrollmentAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), times);

    protected void VerifyConsumedStep(long step) =>
        _work.Verify(work => work.TryConsumeTimeStepAsync(
            UserId, step, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);

    protected void VerifyNoStepConsumed() =>
        _work.Verify(work => work.TryConsumeTimeStepAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);

    protected void VerifyReplaced(Times times) =>
        _work.Verify(work => work.TryReplaceWithActiveSessionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), times);

    protected void VerifyCommitted(bool committed) =>
        _work.Verify(work => work.CommitAsync(It.IsAny<CancellationToken>()), committed ? Times.Once() : Times.Never());

    protected void VerifyDisposed() => _work.Verify(work => work.DisposeAsync(), Times.Once);

    protected readonly Guid _sessionId = Guid.NewGuid();

    protected void SetupSession(AuthLevel authLevel = AuthLevel.Active, DateTimeOffset? now = null)
    {
        var sessionNow = now ?? _now;
        var session = new Session(
            _sessionId, UserId, new byte[32], Guid.NewGuid(),
            authLevel, sessionNow.AddHours(-1), sessionNow.AddHours(8));
        _sessions.Setup(store => store.FindByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    protected void SetupPendingCredentials(string ciphertext, DateTimeOffset? expiresAt = null)
    {
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                PendingTotpSecretCiphertext = ciphertext,
                PendingTotpSecretKeyVersion = 1,
                PendingEnrollmentExpiresAt = expiresAt ?? _now.AddMinutes(5)
            });
    }

    protected void SetupVerification(
        string secret, DateTimeOffset now, long? currentStep = null, DateTimeOffset? lockedUntil = null)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(AuthLevel.MfaPending, now);
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                MfaEnabled = true,
                TotpSecretCiphertext = _protector.Protect(secret),
                TotpSecretKeyVersion = 1,
                LastTotpTimeStep = currentStep,
                MfaLockedUntil = lockedUntil
            });
    }

    protected void SetupConfirmation(string secret, DateTimeOffset now)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(now: now);
        SetupPendingCredentials(_protector.Protect(secret), now.AddMinutes(5));
    }

    protected void VerifyAuditDoesNotContainSecret(
        string expectedReason,
        string eventType = AuditEventTypes.AuthMfaEnabled,
        AuditOutcome outcome = AuditOutcome.Failure)
    {
        var write = _auditWrites.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(eventType));
        Assert.That(write.Draft.Outcome, Is.EqualTo(outcome));
        // The real Audit validator accepts the record with this reason for its event and outcome.
        Assert.That(new Platform.Audit.Application.AuditDraftValidator().Validate(write.Draft, write.Context),
            Is.EqualTo(AuditRejectionCode.None));
        Assert.That(write.Draft.Details, Is.EqualTo(new Dictionary<string, string> { ["reasonCode"] = expectedReason }));
        var serialized = string.Join('|', write.Draft.EventType, write.Draft.Resource?.Id, write.Context.CorrelationId,
            string.Join(',', write.Draft.Details!.Select(pair => $"{pair.Key}={pair.Value}")));
        Assert.That(serialized, Does.Not.Contain("ciphertext").And.Not.Contain("123456").And.Not.Contain("000000"));
    }
}
