using Microsoft.AspNetCore.Http;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Team21.Integration;
using Platform.Api.Services.Identity;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Tests.Api;

public class SessionCurrentActorTests
{
    [TestCase(false)]
    public void ActiveAuthenticatedIdentityMapsUserWithoutRequiringSessionHandle(bool hasSessionHandle)
    {
        var userId = Guid.NewGuid();
        ICurrentActor actor = new SessionCurrentActor(new IdentityContext(new CallerIdentity(
            true, userId, AuthenticationLevel.Active, hasSessionHandle ? Guid.NewGuid() : null)));

        Assert.Multiple(() =>
        {
            Assert.That(actor.UserId, Is.EqualTo(userId));
            Assert.That(actor.IsAuthenticated, Is.True);
        });
    }

    [Test]
    public void MissingIdentityIsAnonymous()
    {
        AssertAnonymous(new SessionCurrentActor(new IdentityContext(null)));
    }

    [TestCase(false, AuthenticationLevel.Active)]
    [TestCase(true, AuthenticationLevel.MfaPending)]
    [TestCase(true, (AuthenticationLevel)999)]
    public void InactiveOrInconsistentAuthenticationStateIsAnonymous(bool authenticated, AuthenticationLevel level)
    {
        var identity = new CallerIdentity(authenticated, Guid.NewGuid(), level, Guid.NewGuid());

        AssertAnonymous(new SessionCurrentActor(new IdentityContext(identity)));
    }

    [TestCase(null)]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void ActiveIdentityWithoutValidUserIdIsAnonymous(string? userId)
    {
        var identity = new CallerIdentity(
            true, userId is null ? null : Guid.Parse(userId), AuthenticationLevel.Active, Guid.NewGuid());

        AssertAnonymous(new SessionCurrentActor(new IdentityContext(identity)));
    }

    [Test]
    public void IdentityPopulatedAfterConstructionIsReadWhenUsed()
    {
        var context = new IdentityContext(null);
        var actor = new SessionCurrentActor(context);
        var userId = Guid.NewGuid();
        context.Current = new CallerIdentity(true, userId, AuthenticationLevel.Active, Guid.NewGuid());

        Assert.That(actor.UserId, Is.EqualTo(userId));
        Assert.That(actor.IsAuthenticated, Is.True);
    }

    [Test]
    public void ClearedIdentityDoesNotLeaveStaleAuthenticatedActor()
    {
        var context = new IdentityContext(new CallerIdentity(
            true, Guid.NewGuid(), AuthenticationLevel.Active, Guid.NewGuid()));
        var actor = new SessionCurrentActor(context);
        Assert.That(actor.IsAuthenticated, Is.True);

        context.Current = null;

        AssertAnonymous(actor);
    }

    [Test]
    public void PlatformIdentityWithoutHttpContextIsAnonymous()
    {
        AssertAnonymous(FromPlatformContext(null));
    }

    [TestCase("X-Test-User")]
    public void ClientHeaderWithoutValidatedIdentityCannotAuthenticate(string headerName)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[headerName] = Guid.NewGuid().ToString();

        AssertAnonymous(FromPlatformContext(http));
    }

    [Test]
    public void ValidatedPlatformUserIsPreservedDespiteConflictingClientHeaders()
    {
        var userId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid().ToString();
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Test-User"] = clientUserId;
        http.Request.Headers["X-Stub-User"] = clientUserId;
        http.Request.Headers["X-User-Id"] = clientUserId;
        http.Request.Headers.Authorization = "Bearer " + clientUserId;

        // Supply the server-owned result of validation, not a cookie or token validator.
        http.Items[SessionCheckMiddleware.SessionItemKey] = new ValidateSession(
            Guid.NewGuid(), userId, AuthenticationLevel.Active);

        var actor = FromPlatformContext(http);

        Assert.Multiple(() =>
        {
            Assert.That(actor.UserId, Is.EqualTo(userId));
            Assert.That(actor.IsAuthenticated, Is.True);
        });
    }

    private static ICurrentActor FromPlatformContext(HttpContext? http) =>
        new SessionCurrentActor(new SessionIdentityContext(new TestHttpContextAccessor(http)));

    private static void AssertAnonymous(ICurrentActor actor)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actor.UserId, Is.Null);
            Assert.That(actor.IsAuthenticated, Is.False);
        });
    }

    private sealed class IdentityContext(CallerIdentity? current) : IIdentityContext
    {
        public CallerIdentity? Current { get; set; } = current;
    }

    private sealed class TestHttpContextAccessor(HttpContext? http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = http;
    }
}
