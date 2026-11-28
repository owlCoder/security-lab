using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Controllers.Identity;
using Platform.Api.Contracts.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Moq;
using Platform.Identity.Interfaces;
using IIdentityContext = Platform.Api.Interfaces.Identity.IIdentityContext;

namespace Platform.Identity.Tests.Controllers;

[TestFixture]
[Category("US-06")]
public class AuthControllerTests
{
    private Mock<ILoginService> _login = null!;
    private Mock<ILogoutService> _logout = null!;
    private Mock<IIdentityContext> _identity = null!;
    private Mock<IMfaService> _mfa = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _login = new Mock<ILoginService>();
        _logout = new Mock<ILogoutService>();
        _identity = new Mock<IIdentityContext>();
        _mfa = new Mock<IMfaService>();
        _controller = new AuthController(_login.Object, _logout.Object, _identity.Object, _mfa.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Test]
    public void NoCaller_Returns401()
    {
        CallerIs(null);

        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void MfaPendingCaller_Returns401()
    {
        CallerIs(new CallerIdentity(false, Guid.NewGuid(), AuthenticationLevel.MfaPending, Guid.NewGuid()));

        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void ActiveCaller_ReturnsOnlyUserIdAndLevel()
    {
        var userId = Guid.NewGuid();
        CallerIs(new CallerIdentity(true, userId, AuthenticationLevel.Active, Guid.NewGuid()));

        var result = _controller.Me() as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = result!.Value as MeResponse;
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.UserId, Is.EqualTo(userId));
        Assert.That(body.Level, Is.EqualTo("Active"));
        Assert.That(_controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    private void CallerIs(CallerIdentity? caller) =>
        _identity.Setup(i => i.Current).Returns(caller);

    [Test]
    public async Task Login_Success_ReturnsLoginResponseAndSetsSessionCookie()
    {
        var userId = Guid.NewGuid();
        const string email = "test@platform.com";
        const string password = "SecurePassword123!";
        const string token = "opaque-session-token-value";
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var http = _controller.HttpContext;
        var request = new LoginRequest(email, password);

        _login
            .Setup(service => service.ExecuteAsync(
                email,
                password,
                http.TraceIdentifier,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(
                LoginOutcome.Success,
                userId,
                new IssuedSession(token, Guid.NewGuid(), expiresAt)));

        var response = await _controller.Login(request, CancellationToken.None);

        var result = response as OkObjectResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(new LoginResponse(userId, false)));
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(http.Response.Headers.SetCookie.ToString(), Does.Contain($"{SessionCookie.Name}={token}"));
        _login.Verify(
            service => service.ExecuteAsync(email, password, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
    }

    [Test]
    public async Task Login_MfaPendingSession_ReturnsMfaRequired()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var http = _controller.HttpContext;
        var request = new LoginRequest("test@platform.com", "SecurePassword123!");
        _login
            .Setup(service => service.ExecuteAsync(
                request.Email, request.Password, http.TraceIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(
                LoginOutcome.Success,
                userId,
                new IssuedSession("pending-session-token", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
                MfaRequired: true));

        // Act
        var response = await _controller.Login(request, CancellationToken.None);

        // Assert
        var result = response as OkObjectResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value, Is.EqualTo(new LoginResponse(userId, true)));
    }

    [TestCase(LoginOutcome.InvalidRequest, 400, "invalid_request")]
    [TestCase(LoginOutcome.Unavailable, 503, "login_unavailable")]
    [TestCase(LoginOutcome.InvalidCredentials, 401, "invalid_credentials")]
    public async Task Login_Failure_ReturnsExpectedStatusAndError(
        LoginOutcome outcome,
        int expectedStatusCode,
        string expectedCode)
    {
        var http = _controller.HttpContext;
        var request = new LoginRequest("test@platform.com", "SecurePassword123!");

        _login
            .Setup(service => service.ExecuteAsync(
                request.Email,
                request.Password,
                http.TraceIdentifier,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(outcome));

        var response = await _controller.Login(request, CancellationToken.None);

        var result = response as ObjectResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.GetType().GetProperty("code")!.GetValue(result.Value),
            Is.EqualTo(expectedCode));
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(http.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    [Test]
    public async Task Logout_RevokesCookieSession_AndClearsTheCookieWithItsAttributes()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;

        var response = await _controller.Logout();

        Assert.That(response, Is.InstanceOf<NoContentResult>());
        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        AssertCookieCleared(http);
    }

    [Test]
    public async Task Logout_ClientDisconnect_DoesNotCancelTheRevocation()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        http.RequestAborted = aborted.Token;
        _controller.ControllerContext.HttpContext = http;

        await _controller.Logout();

        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
    }

    [TestCase(null)]
    [TestCase("unknown-token")]
    public async Task Logout_WithoutValidSession_AnswersTheSame(string? token)
    {
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;

        var response = await _controller.Logout();

        Assert.That(response, Is.InstanceOf<NoContentResult>());
        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
        AssertCookieCleared(http);
    }

    [Test]
    public async Task Logout_WhenRevokingFails_KeepsTheCookie()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;
        _logout
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.Logout());

        Assert.That(http.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    [TestCase(MfaOutcome.Success, 200)]
    [TestCase(MfaOutcome.InvalidRequest, 400)]
    [TestCase(MfaOutcome.Unavailable, 503)]
    [TestCase(MfaOutcome.InvalidCode, 401)]
    public async Task MfaVerify_MapsServiceOutcome(
        MfaOutcome outcome,
        int expectedStatusCode)
    {
        const string token = "pending-session-token";
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;
        var request = new MfaVerifyRequest("123456");
        IssuedSession? issued = outcome == MfaOutcome.Success
            ? new IssuedSession("new-active-token", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8))
            : null;
        _mfa.Setup(service => service.VerifyAsync(
                token, request.Code, http.TraceIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync((outcome, issued));

        var response = await _controller.MfaVerify(request, CancellationToken.None);

        var statusCode = response switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusResult => statusResult.StatusCode,
            _ => 200
        };
        Assert.That(statusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task MfaVerify_Success_SetsTheCookieOfTheNewSession()
    {
        // Arrange
        const string pendingToken = "pending-session-token";
        var http = HttpContextWithCookie(pendingToken);
        _controller.ControllerContext.HttpContext = http;
        var request = new MfaVerifyRequest("123456");
        var issued = new IssuedSession("new-active-token", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(8));
        _mfa.Setup(service => service.VerifyAsync(
                pendingToken, request.Code, http.TraceIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaOutcome.Success, issued));

        // Act
        await _controller.MfaVerify(request, CancellationToken.None);

        // Assert
        var setCookie = http.Response.Headers.SetCookie.ToString();
        Assert.That(setCookie, Does.Contain($"{SessionCookie.Name}={issued.Token}"));
        Assert.That(setCookie, Does.Not.Contain(pendingToken));
    }

    [Test]
    public async Task MfaVerify_Refused_DoesNotSetACookie()
    {
        // Arrange
        const string pendingToken = "pending-session-token";
        var http = HttpContextWithCookie(pendingToken);
        _controller.ControllerContext.HttpContext = http;
        var request = new MfaVerifyRequest("000000");
        _mfa.Setup(service => service.VerifyAsync(
                pendingToken, request.Code, http.TraceIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaOutcome.InvalidCode, (IssuedSession?)null));

        // Act
        await _controller.MfaVerify(request, CancellationToken.None);

        // Assert
        Assert.That(http.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    private static DefaultHttpContext HttpContextWithCookie(string? token)
    {
        var http = new DefaultHttpContext();
        if (token is not null)
            http.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        return http;
    }

    private static void AssertCookieCleared(DefaultHttpContext http)
    {
        var setCookie = http.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.That(setCookie, Does.StartWith(SessionCookie.Name.ToLowerInvariant() + "=;"));
        Assert.That(setCookie, Does.Contain("expires=thu, 01 jan 1970"));
        Assert.That(setCookie,
            Does.Contain("path=/").And.Contain("httponly").And.Contain("secure").And.Contain("samesite=strict"));
    }
}