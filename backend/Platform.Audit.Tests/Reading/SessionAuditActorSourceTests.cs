using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Platform.Api.Enums.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Audit;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests.Reading;

[TestFixture]
[Property("RequirementId", "AUD-01")]
public sealed class SessionAuditActorSourceTests
{
    private static readonly Guid User = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [Test]
    public void ActiveSessionFromSessionCheck_IsTheUserActor()
    {
        var source = Source(http => http.Items[SessionCheckMiddleware.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), User, AuthenticationLevel.Active));

        Assert.That(source.GetCurrentActor(), Is.EqualTo(AuditActor.ForUser(User)));
    }

    [Test]
    public void ActiveSessionFromTheAccessSubjectProvider_IsTheUserActor()
    {
        var source = Source(http => http.Items[RevisionRequestIdentity.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), User, AuthenticationLevel.Active));

        Assert.That(source.GetCurrentActor(), Is.EqualTo(AuditActor.ForUser(User)));
    }

    [Test]
    public void NoSession_IsAnonymous()
    {
        Assert.That(Source(_ => { }).GetCurrentActor(), Is.EqualTo(AuditActor.Anonymous));
    }

    [TestCase(AuthenticationLevel.None)]
    [TestCase(AuthenticationLevel.MfaPending)]
    public void SessionThatIsNotFullyAuthenticated_IsAnonymous(AuthenticationLevel level)
    {
        var source = Source(http => http.Items[SessionCheckMiddleware.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), User, level));

        Assert.That(source.GetCurrentActor(), Is.EqualTo(AuditActor.Anonymous));
    }

    [Test]
    public void SessionWithEmptyUserId_IsAnonymous()
    {
        var source = Source(http => http.Items[SessionCheckMiddleware.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), Guid.Empty, AuthenticationLevel.Active));

        Assert.That(source.GetCurrentActor(), Is.EqualTo(AuditActor.Anonymous));
    }

    [Test]
    public void ClientSuppliedHeadersQueryAndBody_AreNeverUsedAsTheActor()
    {
        var source = Source(http =>
        {
            http.Request.Headers["X-User-Id"] = User.ToString();
            http.Request.Headers["X-Test-User"] = $"{User}:Teacher";
            http.Request.QueryString = new QueryString($"?actorId={User}");
            http.Items["session"] = "not a validated session";
        });

        Assert.That(source.GetCurrentActor(), Is.EqualTo(AuditActor.Anonymous));
    }

    [Test]
    public void OutsideAnHttpRequest_ThereIsNoActor()
    {
        var source = new SessionAuditActorSource(new HttpContextAccessor { HttpContext = null });

        Assert.That(source.GetCurrentActor(), Is.Null);
    }

    private static SessionAuditActorSource Source(Action<HttpContext> arrange)
    {
        var http = new DefaultHttpContext();
        arrange(http);
        return new SessionAuditActorSource(new HttpContextAccessor { HttpContext = http });
    }
}
