using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Platform.Api.Services.Identity;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-06")]
public class SessionCheckMiddlewareTests
{
    private bool _nextCalled;
    private InMemorySessionManagement _sessionManagement = null!;
    private SessionCheckMiddleware _middleware = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionManagement = new InMemorySessionManagement();
        _nextCalled = false;

        RequestDelegate mockNextMiddleware = (Context) =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };

        _middleware = new SessionCheckMiddleware(mockNextMiddleware);
    }

    [Test]
    public async Task UnknownEndpoint_NoCookie_CallNext()
    {
        var context = new DefaultHttpContext();

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));
        Assert.That(_nextCalled, Is.True);
    }

    [Test]
    public async Task PublicEndpoint_NoCookie_CallsNext()
    {
        var endpoint = new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "public");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_NoCookie_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_UnknownToken_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}=nepoznat-token";

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_ValidToken_CallNext_AndStoresSession()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "valid-token";

        _sessionManagement.Seed(userId, token: token);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));

        var session = context.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession;

        Assert.That(session, Is.Not.Null);
        Assert.That(session!.UserId, Is.EqualTo(userId));

    }

    [Test]
    public async Task ProtectedEndpoint_RevokedSession_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "revoked-token";

        var handle = _sessionManagement.Seed(userId, token: token);
        _sessionManagement.RevokeSession(userId, handle);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_ExpiredSession_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "expired-token";

        _sessionManagement.Seed(userId, lifetime: TimeSpan.FromMinutes(-1), token: token);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessionManagement);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }
}