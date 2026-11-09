using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Application;

[TestFixture]
public sealed class SessionRotationServiceTests
{
    private const string PreLoginToken = "pre-login-token-value";
    private const string UserA = "user-a";
    private const string UserB = "user-b";
    private const string CorrelationId = "corr-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AbsoluteExpiry = Now.AddHours(8);
    private static readonly Guid ChainId = new("11111111-2222-3333-4444-555555555555");

    [Test]
    public async Task RotateAsync_AfterLogin_ReturnsNewTokenDifferentFromOld()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.Rotated));
            Assert.That(result.NewToken, Is.Not.Null.And.Not.EqualTo(PreLoginToken));
        });
    }

    [Test]
    public async Task RotateAsync_WhenPreLoginTokenIsReusedAfterLogin_ReturnsSessionRevoked()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);
        await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var reuse = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(reuse.Succeeded, Is.False);
            Assert.That(reuse.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
            Assert.That(reuse.NewToken, Is.Null);
            Assert.That(await fixture.IsActiveAsync(PreLoginToken), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenMfaCompleted_IssuesElevatedSessionAndRevokesOld()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, UserA, SessionPrivilegeLevel.Authenticated);

        var result = await fixture.Service.RotateAsync(new RotateSessionRequest(
            PreLoginToken, UserA, RotationTrigger.MfaCompleted, SessionPrivilegeLevel.Elevated, CorrelationId));

        var newSession = await fixture.FindAsync(result.NewToken);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(newSession?.PrivilegeLevel, Is.EqualTo(SessionPrivilegeLevel.Elevated));
            Assert.That(await fixture.IsActiveAsync(PreLoginToken), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WithNewTokenAfterRotation_Succeeds()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);
        var login = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var stepUp = await fixture.Service.RotateAsync(new RotateSessionRequest(
            login.NewToken, UserA, RotationTrigger.MfaCompleted, SessionPrivilegeLevel.Elevated, CorrelationId));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await fixture.IsActiveAsync(login.NewToken), Is.False);
            Assert.That(stepUp.Succeeded, Is.True);
            Assert.That(await fixture.IsActiveAsync(stepUp.NewToken), Is.True);
        });
    }

    [Test]
    public async Task RotateAsync_KeepsChainAndAbsoluteExpiryOfOriginalSession()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var newSession = await fixture.FindAsync(result.NewToken);
        Assert.Multiple(() =>
        {
            Assert.That(newSession?.ChainId, Is.EqualTo(ChainId));
            Assert.That(newSession?.AbsoluteExpiresAt, Is.EqualTo(AbsoluteExpiry));
            Assert.That(newSession?.IssuedAt, Is.EqualTo(Now));
            Assert.That(newSession?.SubjectId, Is.EqualTo(UserA));
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionReachedAbsoluteExpiry_ReturnsSessionExpiredAndDoesNotRotate()
    {
        var fixture = new Fixture(now: AbsoluteExpiry);
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionExpired));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(fixture.Tokens.Issued, Is.Empty);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionExpiresBeforeStoreRotation_ReturnsSessionExpired()
    {
        var store = new InMemorySessionRotationStore();
        var tokens = new RecordingTokenGenerator();
        var audit = new RecordingAuditWriter();
        store.Add(new RotatableSession(
            ChainId, null, tokens.ComputeHash(PreLoginToken), SessionPrivilegeLevel.PreAuthentication,
            Now.AddMinutes(-5), AbsoluteExpiry));
        var service = new SessionRotationService(
            new ExpiresBeforeRotationStore(store), tokens, audit, new FixedTimeProvider(Now));

        var result = await service.RotateAsync(LoginRequest(PreLoginToken));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionExpired));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(audit.Entries.Single().Outcome, Is.EqualTo(SessionRotationCodes.SessionExpired));
        });
    }

    [Test]
    public async Task RotateAsync_AfterChainIsRevoked_RejectsEveryTokenOfChain()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);
        var login = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        await fixture.Store.RevokeChainAsync(ChainId, Now);

        var withOld = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));
        var withNew = await fixture.Service.RotateAsync(LoginRequest(login.NewToken));
        Assert.Multiple(() =>
        {
            Assert.That(withOld.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
            Assert.That(withNew.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
        });
    }

    [Test]
    public async Task RotateAsync_AfterAllSessionsOfSubjectAreRevoked_RejectsTokenCreatedByRotation()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);
        var login = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        await fixture.Store.RevokeAllForSubjectAsync(UserA, Now);

        var result = await fixture.Service.RotateAsync(LoginRequest(login.NewToken));
        Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionRevoked));
    }

    [Test]
    public async Task RotateAsync_WhenTwoRequestsUseSameOldToken_ExactlyOneSucceeds()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var results = await Task.WhenAll(
            Task.Run(async () => await fixture.Service.RotateAsync(LoginRequest(PreLoginToken))),
            Task.Run(async () => await fixture.Service.RotateAsync(LoginRequest(PreLoginToken))));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result.Succeeded), Is.EqualTo(1));
            Assert.That(
                results.Single(result => !result.Succeeded).Code,
                Is.AnyOf(SessionRotationCodes.RotationConflict, SessionRotationCodes.SessionRevoked));
        });
    }

    [Test]
    public async Task RotateAsync_WhenAuditFails_ReturnsAuditFailedAndRevokesOldAndNewSession()
    {
        var failingAudit = new FailingAuditWriter();
        var fixture = new Fixture(failingAudit);
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var issuedToken = fixture.Tokens.Issued.Single().Value;
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(failingAudit.Attempts, Is.EqualTo(1));
            Assert.That(await fixture.IsActiveAsync(PreLoginToken), Is.False);
            Assert.That(await fixture.IsActiveAsync(issuedToken), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenRotated_RecordsActorTriggerOutcomeAndCorrelationId()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var entry = fixture.Audit.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventType, Is.EqualTo(SessionSecurityAuditEvents.SessionRotated));
            Assert.That(entry.SubjectId, Is.EqualTo(UserA));
            Assert.That(entry.Trigger, Is.EqualTo(RotationTrigger.Login));
            Assert.That(entry.Outcome, Is.EqualTo(SessionRotationCodes.Rotated));
            Assert.That(entry.OccurredAt, Is.EqualTo(Now));
            Assert.That(entry.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task RotateAsync_WhenRejected_RecordsRejectionCodeAsOutcome()
    {
        var fixture = new Fixture();

        await fixture.Service.RotateAsync(LoginRequest("unknown-token"));

        Assert.That(
            fixture.Audit.Entries.Single().Outcome,
            Is.EqualTo(SessionRotationCodes.SessionNotFound));
    }

    [Test]
    public async Task RotateAsync_AuditEntriesAndResults_DoNotContainTokensHashesOrChainId()
    {
        var fixture = new Fixture();
        var seeded = fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);
        var rotated = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));
        var rejected = await fixture.Service.RotateAsync(LoginRequest(PreLoginToken));

        var issued = fixture.Tokens.Issued.Single();
        var secrets = new[] { PreLoginToken, seeded.TokenHash, issued.Value, issued.Hash, ChainId.ToString() };
        var printed = fixture.Audit.Entries.Select(entry => entry.ToString())
            .Append(rotated.ToString())
            .Append(rejected.ToString())
            .ToList();

        Assert.That(
            printed,
            Has.All.Matches<string>(text => secrets.All(secret => !text.Contains(secret, StringComparison.Ordinal))));
    }

    [Test]
    public async Task RotateAsync_WhenTokenIsUnknown_ReturnsSessionNotFound()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RotateAsync(LoginRequest("unknown-token"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SessionNotFound));
            Assert.That(fixture.Tokens.Issued, Is.Empty);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionBelongsToOtherSubject_ReturnsSubjectMismatchAndKeepsSession()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, UserB, SessionPrivilegeLevel.Authenticated);

        var result = await fixture.Service.RotateAsync(new RotateSessionRequest(
            PreLoginToken, UserA, RotationTrigger.MfaCompleted, SessionPrivilegeLevel.Elevated, CorrelationId));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SubjectMismatch));
            Assert.That(await fixture.IsActiveAsync(PreLoginToken), Is.True);
        });
    }

    [Test]
    public async Task RotateAsync_WhenPrivilegeChangeStartsFromPreLoginSession_ReturnsSubjectMismatch()
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(new RotateSessionRequest(
            PreLoginToken, UserA, RotationTrigger.RoleChanged, SessionPrivilegeLevel.Authenticated, CorrelationId));

        Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.SubjectMismatch));
    }

    [Test]
    public async Task RotateAsync_WhenRequestIsNull_ReturnsInvalidRequestAndWritesNoAudit()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RotateAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.InvalidRequest));
            Assert.That(fixture.Audit.Entries, Is.Empty);
        });
    }

    [TestCaseSource(nameof(InvalidRequests))]
    public async Task RotateAsync_WhenRequestFieldIsInvalid_ReturnsInvalidRequestAndWritesNoAudit(
        RotateSessionRequest request)
    {
        var fixture = new Fixture();
        fixture.Seed(PreLoginToken, subjectId: null, SessionPrivilegeLevel.PreAuthentication);

        var result = await fixture.Service.RotateAsync(request);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.InvalidRequest));
            Assert.That(fixture.Audit.Entries, Is.Empty);
            Assert.That(await fixture.IsActiveAsync(PreLoginToken), Is.True);
        });
    }

    private static IEnumerable<TestCaseData> InvalidRequests()
    {
        var valid = LoginRequest(PreLoginToken);
        yield return new TestCaseData(valid with { CurrentToken = null }).SetArgDisplayNames("no token");
        yield return new TestCaseData(valid with { CurrentToken = " " }).SetArgDisplayNames("blank token");
        yield return new TestCaseData(valid with { SubjectId = null }).SetArgDisplayNames("no subject");
        yield return new TestCaseData(valid with { SubjectId = "" }).SetArgDisplayNames("empty subject");
        yield return new TestCaseData(valid with { CorrelationId = null }).SetArgDisplayNames("no correlation ID");
        yield return new TestCaseData(valid with { NewPrivilegeLevel = SessionPrivilegeLevel.PreAuthentication })
            .SetArgDisplayNames("pre-authentication level");
        yield return new TestCaseData(valid with { Trigger = (RotationTrigger)99 }).SetArgDisplayNames("unknown trigger");
    }

    private static RotateSessionRequest LoginRequest(string? token) =>
        new(token, UserA, RotationTrigger.Login, SessionPrivilegeLevel.Authenticated, CorrelationId);

    /// <summary>
    /// Simulates a session that reaches its absolute expiry between FindAsync and RotateAsync:
    /// the store reports Expired from its atomic check.
    /// </summary>
    private sealed class ExpiresBeforeRotationStore(InMemorySessionRotationStore inner) : ISessionRotationStore
    {
        public ValueTask<RotatableSession?> FindAsync(
            string tokenHash, CancellationToken cancellationToken = default)
            => inner.FindAsync(tokenHash, cancellationToken);

        public ValueTask<SessionRotationStoreStatus> RotateAsync(
            string oldTokenHash, RotatableSession replacement, DateTimeOffset now,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SessionRotationStoreStatus.Expired);

        public ValueTask<int> RevokeChainAsync(
            Guid chainId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => inner.RevokeChainAsync(chainId, now, cancellationToken);

        public ValueTask<int> RevokeAllForSubjectAsync(
            string subjectId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => inner.RevokeAllForSubjectAsync(subjectId, now, cancellationToken);
    }

    private sealed class Fixture
    {
        public Fixture(ISessionSecurityAuditWriter? auditWriter = null, DateTimeOffset? now = null)
        {
            Service = new SessionRotationService(
                Store, Tokens, auditWriter ?? Audit, new FixedTimeProvider(now ?? Now));
        }

        public InMemorySessionRotationStore Store { get; } = new();

        public RecordingTokenGenerator Tokens { get; } = new();

        public RecordingAuditWriter Audit { get; } = new();

        public SessionRotationService Service { get; }

        public RotatableSession Seed(string token, string? subjectId, SessionPrivilegeLevel level)
        {
            var session = new RotatableSession(
                ChainId, subjectId, Tokens.ComputeHash(token), level, Now.AddMinutes(-5), AbsoluteExpiry);
            Store.Add(session);
            return session;
        }

        public async Task<RotatableSession?> FindAsync(string? token) =>
            string.IsNullOrEmpty(token) ? null : await Store.FindAsync(Tokens.ComputeHash(token));

        public async Task<bool> IsActiveAsync(string? token) =>
            await FindAsync(token) is { IsRevoked: false };
    }
}
