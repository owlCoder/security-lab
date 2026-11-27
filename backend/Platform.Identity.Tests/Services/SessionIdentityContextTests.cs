using Microsoft.AspNetCore.Http;
using Platform.Identity.Enums;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-06")]
public class SessionIdentityContextTests
{
    private DefaultHttpContext _context = null!;
    private SessionIdentityContext _identity = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = _context };
        _identity = new SessionIdentityContext(accessor);
    }

    [Test]
    public void NoSession_ReturnsNull()
    {
        Assert.That(_identity.Current, Is.Null);
    }

    [Test]
    public void ActiveSession_IsAuthenticated_WithUserAndHandle()
    {
        var userId = Guid.NewGuid();
        var handle = Guid.NewGuid();
        _context.Items[SessionCheckMiddleware.SessionItemKey] = new ValidateSession(handle, userId, AuthLevel.Active);

        var caller = _identity.Current;

        Assert.That(caller, Is.Not.Null);
        Assert.That(caller!.IsAuthenticated, Is.True);
        Assert.That(caller.UserId, Is.EqualTo(userId));
        Assert.That(caller.Level, Is.EqualTo(AuthLevel.Active));
        Assert.That(caller.CurrentSessionHandle, Is.EqualTo(handle));
    }

    [Test]
    public void MfaPendingSession_IsNotAuthenticated_ButHasUserId()
    {
        var userId = Guid.NewGuid();
        _context.Items[SessionCheckMiddleware.SessionItemKey] = new ValidateSession(Guid.NewGuid(), userId, AuthLevel.MfaPending);

        var caller = _identity.Current;

        Assert.That(caller, Is.Not.Null);
        Assert.That(caller!.IsAuthenticated, Is.False);
        Assert.That(caller.UserId, Is.EqualTo(userId));
        Assert.That(caller.Level, Is.EqualTo(AuthLevel.MfaPending));
    }
}
