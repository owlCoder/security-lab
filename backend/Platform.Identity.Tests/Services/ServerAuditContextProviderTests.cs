using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Audit;
using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class ServerAuditContextProviderTests
{
    [Test]
    public void AuthenticatedRequest_UsesValidatedSessionActorAndServerCorrelation()
    {
        var userId = Guid.NewGuid();
        var provider = new ServerAuditContextProvider(
            new StubIdentityContext(new CallerIdentity(
                true, userId, AuthenticationLevel.Active, Guid.NewGuid())),
            new StubCorrelationContext("server-correlation"));

        var context = provider.GetCurrentContext();

        Assert.Multiple(() =>
        {
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.Actor, Is.EqualTo(AuditActor.ForUser(userId)));
            Assert.That(context.CorrelationId, Is.EqualTo("server-correlation"));
        });
    }

    [Test]
    public void RequestWithoutValidatedSession_UsesAnonymousActor()
    {
        var provider = new ServerAuditContextProvider(
            new StubIdentityContext(null),
            new StubCorrelationContext("server-correlation"));

        Assert.That(provider.GetCurrentContext()!.Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void RequestWithoutServerCorrelation_HasNoAuditContext(string? correlationId)
    {
        var provider = new ServerAuditContextProvider(
            new StubIdentityContext(null),
            new StubCorrelationContext(correlationId));

        Assert.That(provider.GetCurrentContext(), Is.Null);
    }

    private sealed class StubIdentityContext(CallerIdentity? current) : IIdentityContext
    {
        public CallerIdentity? Current { get; } = current;
    }

    private sealed class StubCorrelationContext(string? correlationId) : ICorrelationContext
    {
        public string? CorrelationId { get; } = correlationId;
    }
}
