using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Controllers.Identity;
using Platform.Api.Domain.Authentication;
using Platform.Api.Services.Identity;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-04")]
public class LoginTests
{
    private const string Email = "student@example.com";
    private const string Password = "S3cret-pass-for-tests";
    private const string Token = "opaque-session-token-value";

    private FakeVerifier _verifier = null!;
    private FakeSessions _sessions = null!;
    private RecordingAudit _audit = null!;
    private LoginAccount _login = null!;

    [SetUp]
    public void SetUp()
    {
        _verifier = new FakeVerifier();
        _sessions = new FakeSessions();
        _audit = new RecordingAudit();
        _login = new LoginAccount(_verifier, _sessions, _audit, TimeProvider.System);
    }

    [Test]
    public async Task ValidCredentials_ActiveAccount_CreatesActiveSession_AndAuditsSuccess()
    {
        var id = Guid.NewGuid();
        _verifier.Result = new CredentialResult(true, id, AccountStatus.Active);

        var result = await _login.ExecuteAsync(Email, Password, "corr-1");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
        Assert.That(result.UserId, Is.EqualTo(id));
        Assert.That(result.Session!.Token, Is.EqualTo(Token));
        Assert.That(_sessions.Calls, Is.EqualTo(1));
        Assert.That(_sessions.LastUser, Is.EqualTo(id));
        Assert.That(_sessions.LastLevel, Is.EqualTo(AuthenticationLevel.Active));
        Assert.That(_audit.Events.Single(), Has.Property("Outcome").EqualTo("success").And.Property("ActorId").EqualTo(id));
    }

    [Test]
    public async Task InvalidCredentials_NoSession_GenericOutcome_AuditedAsFailure()
    {
        var result = await _login.ExecuteAsync(Email, Password, "corr-2");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        Assert.That(_sessions.Calls, Is.EqualTo(0));
        Assert.That(_audit.Events.Single().Outcome, Is.EqualTo("failure"));
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task BlockedOrArchived_WithCorrectPassword_IsRefused_LikeAWrongPassword(AccountStatus status)
    {
        var id = Guid.NewGuid();
        _verifier.Result = new CredentialResult(true, id, status);

        var result = await _login.ExecuteAsync(Email, Password, "corr-3");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(result.Session, Is.Null);
        Assert.That(_sessions.Calls, Is.EqualTo(0));
        Assert.That(_audit.Events.Single(), Has.Property("Outcome").EqualTo("denied").And.Property("ActorId").EqualTo(id));
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
        Assert.That(_verifier.Calls, Is.EqualTo(0));
        Assert.That(_sessions.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task OversizedInput_IsInvalidRequest_AndNothingIsChecked()
    {
        var longEmail = new string('a', CredentialVerifier.MaxEmailLength) + "@example.com";
        var longPassword = new string('p', CredentialVerifier.MaxPasswordLength + 1);

        Assert.That((await _login.ExecuteAsync(longEmail, Password, "c")).Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
        Assert.That((await _login.ExecuteAsync(Email, longPassword, "c")).Outcome, Is.EqualTo(LoginOutcome.InvalidRequest));
        Assert.That(_verifier.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task SessionStoreUnavailable_IsReported_AndNoSuccessIsAudited()
    {
        _verifier.Result = new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active);
        _sessions.Next = null;

        var result = await _login.ExecuteAsync(Email, Password, "corr-5");

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(_audit.Events.Single().Outcome, Is.EqualTo("failure"));
    }

    [Test]
    public async Task AuditEvents_NeverContainEmailPasswordOrToken()
    {
        _verifier.Result = new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active);
        await _login.ExecuteAsync(Email, Password, "corr-6");
        _verifier.Result = CredentialResult.Invalid;
        await _login.ExecuteAsync(Email, Password, "corr-7");

        foreach (var e in _audit.Events)
        {
            var text = string.Join('|', e.Action, e.Target, e.Outcome, e.CorrelationId, e.Detail);
            Assert.That(text, Does.Not.Contain(Password).And.Not.Contain(Email).And.Not.Contain(Token));
        }
    }

    [Test]
    public async Task Controller_Success_SetsHttpOnlySecureStrictCookie_AndNeverPutsTokenInBody()
    {
        _verifier.Result = new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active);
        var controller = ControllerWithHttpContext(out var http);

        var response = await controller.Login(new LoginRequest(Email, Password), CancellationToken.None);

        var ok = (OkObjectResult)response;
        Assert.That(System.Text.Json.JsonSerializer.Serialize(ok.Value), Does.Not.Contain(Token));

        var setCookie = http.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.That(setCookie, Does.Contain(SessionCookie.Name.ToLowerInvariant() + "=" + Token));
        Assert.That(setCookie, Does.Contain("httponly").And.Contain("secure").And.Contain("samesite=strict"));
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task Controller_Failures_ReturnExpectedStatus_AndNoCookie()
    {
        var controller = ControllerWithHttpContext(out var http);

        var wrong = await controller.Login(new LoginRequest(Email, Password), CancellationToken.None);
        var malformed = await controller.Login(new LoginRequest(null, null), CancellationToken.None);

        _verifier.Result = new CredentialResult(true, Guid.NewGuid(), AccountStatus.Active);
        _sessions.Next = null;
        var unavailable = await controller.Login(new LoginRequest(Email, Password), CancellationToken.None);

        Assert.That(wrong, Is.InstanceOf<UnauthorizedObjectResult>());
        Assert.That(malformed, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(((ObjectResult)unavailable).StatusCode, Is.EqualTo(503));
        Assert.That(http.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    private LoginController ControllerWithHttpContext(out DefaultHttpContext http)
    {
        http = new DefaultHttpContext();
        return new LoginController(_login) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private sealed class FakeVerifier : ICredentialVerifier
    {
        public CredentialResult Result { get; set; } = CredentialResult.Invalid;
        public int Calls { get; private set; }

        public Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSessions : ISessionCreator
    {
        public IssuedSession? Next { get; set; } = new(Token, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8));
        public int Calls { get; private set; }
        public Guid? LastUser { get; private set; }
        public AuthenticationLevel? LastLevel { get; private set; }

        public Task<IssuedSession?> CreateAsync(Guid userId, AuthenticationLevel level, CancellationToken ct = default)
        {
            Calls++;
            LastUser = userId;
            LastLevel = level;
            return Task.FromResult(Next);
        }
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public void Write(AuditEvent e) => Events.Add(e);
    }
}
