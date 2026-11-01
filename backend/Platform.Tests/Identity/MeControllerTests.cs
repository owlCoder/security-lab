using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Controllers.Identity;
using Platform.Api.Domain.Authentication;
using Platform.Api.Services.Identity;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-06")]
public class MeControllerTests
{
    private FakeIdentityContext _identity = null!;
    private MeController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _identity = new FakeIdentityContext();
        _controller = new MeController(_identity)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Test]
    public void NoCaller_Returns401()
    {
        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void MfaPendingCaller_Returns401()
    {
        _identity.UserId = Guid.NewGuid();
        _identity.Level = AuthenticationLevel.MfaPending;

        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void ActiveCaller_ReturnsOnlyUserIdAndLevel()
    {
        var userId = Guid.NewGuid();
        _identity.UserId = userId;
        _identity.Level = AuthenticationLevel.Active;
        _identity.CurrentSessionHandle = Guid.NewGuid();

        var result = _controller.Me() as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = result!.Value!;
        var names = body.GetType().GetProperties().Select(p => p.Name).ToArray();
        Assert.That(names, Is.EquivalentTo(new[] { "userId", "level" }));
        Assert.That(body.GetType().GetProperty("userId")!.GetValue(body), Is.EqualTo(userId));
        Assert.That(body.GetType().GetProperty("level")!.GetValue(body), Is.EqualTo("Active"));
        Assert.That(_controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }
}
