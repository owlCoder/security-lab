using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Tests.Services;

/// <summary>IDN-08: validated OIDC principal -> one account -> the same session and audit as password login.</summary>
[TestFixture]
[Category("IDN-08")]
public class FederatedLoginServiceTests
{
    private const string Issuer = "http://localhost:8081/realms/security-lab";
    private const string Subject = "3f6c0a52-7d1e-4b8a-9c2f-000000000001";
    private const string Email = "student@example.com";
    private const string Token = "opaque-session-token-value";

    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private Mock<IExternalIdentityLinker> _linker = null!;
    private Mock<ISessionCreator> _sessions = null!;
    private Mock<ISessionManager> _sessionManager = null!;
    private Mock<ITrustedAuditWriter> _audit = null!;
    private List<(AuditEventDraft Draft, AuditContext Context)> _events = null!;
    private ExternalIdentity? _seenIdentity;
    private FederatedLoginService _login = null!;

    [SetUp]
    public void SetUp()
    {
        _linker = new Mock<IExternalIdentityLinker>();
        _sessions = new Mock<ISessionCreator>();
        _sessionManager = new Mock<ISessionManager>();
        _sessionManager
            .Setup(m => m.RevokeSession(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _events = [];
        _audit = new Mock<ITrustedAuditWriter>();
        GivenAudit(AuditWriteResult.Recorded(Guid.NewGuid()));

        _seenIdentity = null;
        GivenLink(FederatedLoginResult.Existing(AccountId));
        GivenSession(new IssuedSession(Token, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8)));

        _login = new FederatedLoginService(
            _linker.Object,
            _sessions.Object,
            _sessionManager.Object,
            new IdentityAuditProducer(_audit.Object, Mock.Of<ITransactionalAuditWriter>(), NullLogger<IdentityAuditProducer>.Instance));
    }

    // ---------- Success ----------

    [TestCase(FederatedLoginOutcome.Linked)]
    [TestCase(FederatedLoginOutcome.NewlyLinked)]
    public async Task LinkedIdentity_CreatesActiveSession_AndAuditsSuccess(FederatedLoginOutcome outcome)
    {
        GivenLink(new FederatedLoginResult(outcome, AccountId));

        var result = await _login.ExecuteAsync(Principal(), "corr-1");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
        Assert.That(result.UserId, Is.EqualTo(AccountId));
        Assert.That(result.Session!.Token, Is.EqualTo(Token));
        _sessions.Verify(s => s.CreateAsync(AccountId, AuthenticationLevel.Active, It.IsAny<CancellationToken>()), Times.Once());

        var e = _events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(e.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLogin));
            Assert.That(e.Draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(e.Context.Actor.Id, Is.EqualTo(AccountId));
            Assert.That(e.Context.CorrelationId, Is.EqualTo("corr-1"));
        });
    }

    // ---------- Reading the validated token ----------

    [Test]
    public async Task IssuerComesFromTheValidatedClaim_AndClaimsArePassedToTheLinker()
    {
        await _login.ExecuteAsync(Principal(), "corr-2");

        Assert.That(_seenIdentity, Is.EqualTo(new ExternalIdentity(Issuer, Subject, Email, EmailVerified: true)));
    }

    [TestCase(null)]
    [TestCase("false")]
    [TestCase("yes")]
    public async Task EmailIsVerifiedOnlyWhenTheClaimIsTrue(string? emailVerified)
    {
        await _login.ExecuteAsync(Principal(emailVerified: emailVerified), "corr-3");

        Assert.That(_seenIdentity!.EmailVerified, Is.False);
    }

    [Test]
    public async Task MissingSubject_IsRefused_WithoutCallingTheLinker()
    {
        var result = await _login.ExecuteAsync(Principal(subject: null), "corr-4");

        AssertGenericRefusal(result);
        _linker.Verify(l => l.ResolveAsync(It.IsAny<ExternalIdentity>(), It.IsAny<CancellationToken>()), Times.Never());
        AssertFailureAudit("oidc_invalid_identity", expectedActor: null);
    }

    [Test]
    public async Task SubjectWithoutTokenIssuer_IsRefused()
    {
        // A claim created by the application itself has the default issuer, not one from a validated id_token.
        var result = await _login.ExecuteAsync(Principal(issuer: ClaimsIdentity.DefaultIssuer), "corr-5");

        AssertGenericRefusal(result);
        _linker.Verify(l => l.ResolveAsync(It.IsAny<ExternalIdentity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // ---------- Refusals: same answer for the client, reason only in the audit ----------

    [TestCase(FederatedLoginOutcome.InvalidIdentity, "oidc_invalid_identity")]
    [TestCase(FederatedLoginOutcome.EmailNotVerified, "oidc_email_not_verified")]
    [TestCase(FederatedLoginOutcome.NoMatchingAccount, "oidc_no_account")]
    [TestCase(FederatedLoginOutcome.AccountAlreadyLinked, "oidc_account_already_linked")]
    public async Task RefusedIdentity_GetsTheGenericAnswer_AndNoSession(FederatedLoginOutcome outcome, string reasonCode)
    {
        GivenLink(FederatedLoginResult.Refused(outcome));

        var result = await _login.ExecuteAsync(Principal(), "corr-6");

        AssertGenericRefusal(result);
        AssertFailureAudit(reasonCode, expectedActor: null);
    }

    [Test]
    public async Task BlockedAccount_GetsNoSession_AndTheAuditNamesTheConfirmedAccount()
    {
        _sessions
            .Setup(s => s.CreateAsync(AccountId, It.IsAny<AuthenticationLevel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountNotActiveException(AccountId));

        var result = await _login.ExecuteAsync(Principal(), "corr-7");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        AssertFailureAudit("account_inactive", expectedActor: AccountId);
    }

    // ---------- Fail closed ----------

    [Test]
    public async Task SessionStoreUnavailable_ReturnsUnavailable()
    {
        GivenSession(null);

        var result = await _login.ExecuteAsync(Principal(), "corr-8");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(result.Session, Is.Null);
        AssertFailureAudit("session_unavailable", expectedActor: AccountId);
    }

    [Test]
    public async Task SuccessThatCannotBeAudited_RevokesTheNewSession()
    {
        var session = new IssuedSession(Token, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8));
        GivenSession(session);
        GivenAudit(AuditWriteResult.Unavailable());

        var result = await _login.ExecuteAsync(Principal(), "corr-9");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(result.Session, Is.Null);
        _sessionManager.Verify(m => m.RevokeSession(AccountId, session.Handle, It.IsAny<CancellationToken>()), Times.Once());
    }

    // ---------- Helpers ----------

    private static ClaimsPrincipal Principal(
        string? subject = Subject,
        string issuer = Issuer,
        string? email = Email,
        string? emailVerified = "true")
    {
        var claims = new List<Claim>();
        if (subject is not null) claims.Add(new Claim("sub", subject, ClaimValueTypes.String, issuer));
        if (email is not null) claims.Add(new Claim("email", email, ClaimValueTypes.String, issuer));
        if (emailVerified is not null) claims.Add(new Claim("email_verified", emailVerified, ClaimValueTypes.Boolean, issuer));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "oidc-test"));
    }

    private void GivenLink(FederatedLoginResult result) =>
        _linker
            .Setup(l => l.ResolveAsync(It.IsAny<ExternalIdentity>(), It.IsAny<CancellationToken>()))
            .Callback<ExternalIdentity, CancellationToken>((identity, _) => _seenIdentity = identity)
            .ReturnsAsync(result);

    private void GivenSession(IssuedSession? session) =>
        _sessions
            .Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<AuthenticationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    private void GivenAudit(AuditWriteResult result) =>
        _audit
            .Setup(a => a.WriteAsync(It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventDraft, AuditContext, CancellationToken>((draft, context, _) => _events.Add((draft, context)))
            .ReturnsAsync(result);

    private void AssertGenericRefusal(LoginResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
            Assert.That(result.UserId, Is.Null);
            Assert.That(result.Session, Is.Null);
        });
        _sessions.Verify(
            s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<AuthenticationLevel>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    private void AssertFailureAudit(string reasonCode, Guid? expectedActor)
    {
        var e = _events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(e.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthLogin));
            Assert.That(e.Draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(e.Draft.Details, Is.EquivalentTo(new Dictionary<string, string> { ["reasonCode"] = reasonCode }));
            Assert.That(e.Context.Actor.Id, Is.EqualTo(expectedActor));
        });
    }
}