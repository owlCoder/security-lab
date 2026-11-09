using Microsoft.AspNetCore.Http;
using Team21.Acc.Api.Services;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Tests.Api;

public class HeaderCurrentActorTests
{
    private static ICurrentActor ActorFor(params string[] headerValues)
    {
        var context = new DefaultHttpContext();
        if (headerValues.Length > 0)
            context.Request.Headers[HeaderCurrentActor.HeaderName] = headerValues;

        return new HeaderCurrentActor(new HttpContextAccessor { HttpContext = context });
    }

    [Test]
    public void ValidGuidIsTheCaller()
    {
        var id = Guid.NewGuid();

        var actor = ActorFor(id.ToString());

        Assert.That(actor.UserId, Is.EqualTo(id));
        Assert.That(actor.IsAuthenticated, Is.True);
    }

    [Test]
    public void NoHeaderIsAnonymous()
    {
        Assert.That(ActorFor().IsAuthenticated, Is.False);
    }

    [TestCase("")]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void BadValueIsAnonymous(string value)
    {
        Assert.That(ActorFor(value).IsAuthenticated, Is.False);
    }

    [Test]
    public void TwoHeadersAreAnonymous()
    {
        var actor = ActorFor(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        Assert.That(actor.IsAuthenticated, Is.False);
    }

    [Test]
    public void NoRequestIsAnonymous()
    {
        var actor = new HeaderCurrentActor(new HttpContextAccessor());

        Assert.That(actor.IsAuthenticated, Is.False);
    }

    [Test]
    public void AnonymousActorIsNeverSignedIn()
    {
        Assert.That(new AnonymousCurrentActor().IsAuthenticated, Is.False);
    }
}
