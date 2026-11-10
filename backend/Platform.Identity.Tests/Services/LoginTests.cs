using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-04")]
public class LoginTests
{
    private const string Email = "student@example.com";
    private const string Password = "S3cret-pass-for-tests";
    private const string Token = "opaque-session-token-value";

    // Passwords the login flow must handle the same way: ASCII, Cyrillic, mixed scripts, special characters.
    private static readonly string[] Passwords =
    [
        Password,
        "бурек је најјачи искрено",
        "thisIsAnotherPasswordTestWohooo",
        "Pisi kako читаш citaj kako пишеш",
        "p@$$w0rd!\"'<>&%\\"
    ];

    private Mock<ICredentialVerifier> _verifier = null!;
    private Mock<ISessionCreator> _sessions = null!;
    private Mock<IAuditWriter> _audit = null!;
    private List<AuditEvent> _events = null!;
    private LoginService _login = null!;

    [SetUp]
    public void SetUp()
    {
        _verifier = new Mock<ICredentialVerifier>();
        _sessions = new Mock<ISessionCreator>();
        _audit = new Mock<IAuditWriter>();
        _events = [];
        _audit.Setup(a => a.Write(It.IsAny<AuditEvent>())).Callback<AuditEvent>(_events.Add);

        // Same defaults the old fakes had: wrong credentials, and a session store that issues a session.
        GivenCredentials(CredentialResult.Invalid);
        GivenSession(new IssuedSession(Token, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8)));

        _login = new LoginService(_verifier.Object, _sessions.Object, _audit.Object, TimeProvider.System);
    }

    private void GivenCredentials(CredentialResult result) =>
        _verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void GivenSession(IssuedSession? session) =>
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<AuthenticationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    private void VerifyNothingChecked() =>
        _verifier.Verify(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());

    private void VerifyNoSessionCreated() =>
        _sessions.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<AuthenticationLevel>(), It.IsAny<CancellationToken>()), Times.Never());

    [TestCaseSource(nameof(Passwords))]
    public async Task ValidCredentials_ActiveAccount_CreatesActiveSession_AndAuditsSuccess(string password)
    {
        var id = Guid.NewGuid();
        GivenCredentials(new CredentialResult(true, id, AccountStatus.Active));

        var result = await _login.ExecuteAsync(Email, password, "corr-1");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
        Assert.That(result.UserId, Is.EqualTo(id));
        Assert.That(result.Session!.Token, Is.EqualTo(Token));
        _verifier.Verify(v => v.VerifyAsync(Email, password, It.IsAny<CancellationToken>()), Times.Once());
        _sessions.Verify(s => s.CreateAsync(id, AuthenticationLevel.Active, It.IsAny<CancellationToken>()), Times.Once());
        var e = _events.Single();
        Assert.That(e.Outcome, Is.EqualTo("success"));
        Assert.That(e.ActorId, Is.EqualTo(id));
    }

    [Test]
    public async Task InvalidCredentials_NoSession_GenericOutcome_AuditedAsFailure()
    {
        var result = await _login.ExecuteAsync(Email, Password, "corr-2");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        VerifyNoSessionCreated();
        Assert.That(_events.Single().Outcome, Is.EqualTo("failure"));
    }

    [Test]
    [Category("IDN-05")]
    public async Task LockedAccount_IsRefused_LikeAWrongPassword_AndOnlyTheAuditDiffers()
    {
        var id = Guid.NewGuid();
        GivenCredentials(CredentialResult.LockedOut(id));

        var result = await _login.ExecuteAsync(Email, Password, "corr-locked");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        VerifyNoSessionCreated();
        var e = _events.Single();
        Assert.That(e.Outcome, Is.EqualTo("failure"));
        Assert.That(e.Detail, Is.EqualTo("account locked"));
        Assert.That(e.Target, Is.EqualTo(id.ToString()));
        Assert.That(e.ActorId, Is.Null, "the caller is not proven to own the account");
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task BlockedOrArchived_WithCorrectPassword_IsRefused_LikeAWrongPassword(AccountStatus status)
    {
        var id = Guid.NewGuid();
        GivenCredentials(new CredentialResult(true, id, status));

        var result = await _login.ExecuteAsync(Email, Password, "corr-3");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        VerifyNoSessionCreated();
        var e = _events.Single();
        Assert.That(e.Outcome, Is.EqualTo("denied"));
        Assert.That(e.ActorId, Is.EqualTo(id));
    }

    [TestCase(null, Password)]
    [TestCase("", Password)]
    [TestCase("   ", Password)]
    [TestCase(Email, null)]
    [TestCase(Email, "")]
    public async Task MissingInput_IsInvalidRequest_AndNothingIsChecked(string? email, string? password)
    {
        var result = await _login.ExecuteAsync(email, password, "corr-4");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
        VerifyNothingChecked();
        VerifyNoSessionCreated();
    }

    [Test]
    public async Task OversizedEmail_IsInvalidRequest_AndNothingIsChecked()
    {
        var longEmail = new string('a', CredentialVerifier.MaxEmailLength) + "@example.com";

        var result = await _login.ExecuteAsync(longEmail, Password, "corr-5");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
        VerifyNothingChecked();
    }

    [Test]
    public async Task OversizedPassword_IsInvalidRequest_AndNothingIsChecked()
    {
        var longPassword = new string('p', CredentialVerifier.MaxPasswordLength + 1);

        var result = await _login.ExecuteAsync(Email, longPassword, "corr-6");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
        VerifyNothingChecked();
    }

    [Test]
    public async Task SessionStoreUnavailable_IsReported_AndNoSuccessIsAudited()
    {
        GivenCredentials(new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active));
        GivenSession(null);

        var result = await _login.ExecuteAsync(Email, Password, "corr-7");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(_events.Single().Outcome, Is.EqualTo("failure"));
    }

    [Test]
    [Category("IDN-03")]
    public async Task AccountBlockedAfterTheStatusCheck_IsRefused_LikeAnyBlockedLogin_AndNoSuccessIsAudited()
    {
        var id = Guid.NewGuid();
        GivenCredentials(new CredentialResult(true, id, AccountStatus.Active));
        _sessions
            .Setup(s => s.CreateAsync(id, AuthenticationLevel.Active, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountNotActiveException(id));

        var result = await _login.ExecuteAsync(Email, Password, "corr-raced");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        var e = _events.Single();
        Assert.That(e.Outcome, Is.EqualTo("denied"));
        Assert.That(e.ActorId, Is.EqualTo(id));
    }

    [TestCaseSource(nameof(Passwords))]
    public async Task AuditEvents_NeverContainEmailPasswordOrToken(string password)
    {
        GivenCredentials(new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active));
        await _login.ExecuteAsync(Email, password, "corr-8");
        GivenCredentials(CredentialResult.Invalid);
        await _login.ExecuteAsync(Email, password, "corr-9");

        Assert.That(_events, Has.Count.EqualTo(2));
        foreach (var e in _events)
        {
            var text = string.Join('|', e.Action, e.Target, e.Outcome, e.CorrelationId, e.Detail);
            Assert.That(text, Does.Not.Contain(password).And.Not.Contain(Email).And.Not.Contain(Token));
        }
    }
}
