using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Models;

// IDN-22 TIM 09
namespace Platform.Identity.Tests.Services.Login;

/// <summary>
/// IDN-22 / AUD-01: every auth.login event of a password login carries the client address the caller passes
/// in, for a success and for every refusal. The typed e-mail and password never reach the audit details.
/// </summary>
[TestFixture]
[Category("IDN-22")]
public sealed class LoginServiceSourceAddressTests
{
    private const string CorrelationId = "corr-login-1";
    private const string SourceAddressDetail = "sourceAddress";
    private const string Email = "user-a@example.com";
    private const string Password = "synthetic-password-2026";

    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private Mock<ICredentialVerifier> _credentials = null!;
    private Mock<ISessionCreator> _sessions = null!;
    private Mock<IMfaCredentialsStore> _mfaCredentials = null!;
    private List<(AuditEventDraft Draft, AuditContext Context)> _writes = null!;
    private LoginService _login = null!;

    [SetUp]
    public void SetUp()
    {
        _credentials = new Mock<ICredentialVerifier>(MockBehavior.Strict);
        _sessions = new Mock<ISessionCreator>(MockBehavior.Strict);
        _mfaCredentials = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        _writes = [];
        var writer = new Mock<ITrustedAuditWriter>(MockBehavior.Strict);
        writer.Setup(w => w.WriteAsync(
                It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventDraft, AuditContext, CancellationToken>(
                (draft, context, _) => _writes.Add((draft, context)))
            .ReturnsAsync(AuditWriteResult.Recorded(Guid.Parse("66666666-0000-4000-8000-000000000001")));

        // The audit write succeeds in these tests, so login never revokes a session; auth.login is never
        // written inside a caller's transaction.
        var sessionManager = new Mock<ISessionManager>(MockBehavior.Strict);
        var transactionalWriter = new Mock<ITransactionalAuditWriter>(MockBehavior.Strict);
        _login = new LoginService(
            _credentials.Object,
            _sessions.Object,
            sessionManager.Object,
            _mfaCredentials.Object,
            new IdentityAuditProducer(
                writer.Object, transactionalWriter.Object, NullLogger<IdentityAuditProducer>.Instance));
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulLogin_ShouldAuditSourceAddress()
    {
        // Arrange
        SetupActiveAccountWithSession();

        // Act
        var result = await _login.ExecuteAsync(Email, Password, CorrelationId, "203.0.113.7");

        // Assert
        var draft = _writes.Single().Draft;
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(draft.Details, Does.ContainKey(SourceAddressDetail).WithValue("203.0.113.7"));
        });
    }

    [Test]
    public async Task ExecuteAsync_WrongPassword_ShouldAuditSourceAddressForAnonymousActor()
    {
        // Arrange
        SetupCredentials(CredentialResult.Invalid);

        // Act
        var result = await _login.ExecuteAsync(Email, "wrong-password-2026", CorrelationId, "2001:db8::1");

        // Assert
        var (draft, context) = _writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(context.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(draft.Details, Does.ContainKey(SourceAddressDetail).WithValue("2001:db8::1"));
        });
    }

    [Test]
    public async Task ExecuteAsync_LockedAccount_ShouldAuditSourceAddressWithAccountResource()
    {
        // Arrange
        SetupCredentials(CredentialResult.LockedOut(UserId));

        // Act
        var result = await _login.ExecuteAsync(Email, Password, CorrelationId, "198.51.100.4");

        // Assert
        var (draft, context) = _writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
            Assert.That(draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Account, UserId.ToString())));
            Assert.That(context.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(draft.Details, Does.ContainKey(SourceAddressDetail).WithValue("198.51.100.4"));
        });
    }

    [Test]
    public async Task ExecuteAsync_InvalidRequest_ShouldAuditSourceAddress_AndNotVerifyCredentials()
    {
        // Arrange: the strict credential mock has no setup, so a verification would throw.

        // Act
        var result = await _login.ExecuteAsync(email: null, Password, CorrelationId, "198.51.100.4");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
            Assert.That(_writes.Single().Draft.Details, Does.ContainKey(SourceAddressDetail).WithValue("198.51.100.4"));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithoutSourceAddress_ShouldAuditLoginWithoutDetails()
    {
        // Arrange
        SetupActiveAccountWithSession();

        // Act
        var result = await _login.ExecuteAsync(Email, Password, CorrelationId, sourceAddress: null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
            Assert.That(_writes.Single().Draft.Details, Is.Null);
        });
    }

    [Test]
    public async Task ExecuteAsync_FailedLogin_ShouldNotAuditEmailOrPassword()
    {
        // Arrange
        SetupCredentials(CredentialResult.Invalid);

        // Act
        await _login.ExecuteAsync(Email, Password, CorrelationId, "203.0.113.7");

        // Assert
        var values = string.Join("|", _writes.Single().Draft.Details!.Values); // A failure has a reason code.
        Assert.That(values, Does.Not.Contain(Email).And.Not.Contain(Password));
    }

    private void SetupCredentials(CredentialResult result) =>
        _credentials.Setup(c => c.VerifyAsync(Email, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void SetupActiveAccountWithSession()
    {
        SetupCredentials(new CredentialResult(true, UserId, AccountStatus.Active));
        _mfaCredentials.Setup(m => m.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _sessions.Setup(s => s.CreateAsync(UserId, AuthLevel.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedSession(
                "synthetic-session-token", Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001"), Now.AddHours(8)));
    }
}
