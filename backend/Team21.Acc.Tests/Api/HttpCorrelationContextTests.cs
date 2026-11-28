using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Team21.Acc.Api;
using Team21.Acc.Api.Services;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Tests.Api;

[NonParallelizable]
public class HttpCorrelationContextTests
{
    private const string ClientHeader = "X-Correlation-ID";

    [TearDown]
    public void ClearHttpContext() => new HttpContextAccessor().HttpContext = null;

    [TestCase("server-request-42")]
    public void UsesServerTraceIdentifierWithoutRequiringGuidFormat(string? serverId)
    {
        var http = new DefaultHttpContext();
        if (serverId is not null)
            http.TraceIdentifier = serverId;

        var expected = http.TraceIdentifier;
        ICorrelationContext correlation = new HttpCorrelationContext(
            new HttpContextAccessor { HttpContext = http });

        Assert.That(expected, Is.Not.Empty);
        Assert.That(correlation.CorrelationId, Is.EqualTo(expected));
    }

    [TestCase("attacker-chosen-id")]
    public void ClientHeaderCannotOverrideServerTraceIdentifier(string clientId)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trusted-server-id" };
        http.Request.Headers[ClientHeader] = clientId;

        var correlation = new HttpCorrelationContext(
            new HttpContextAccessor { HttpContext = http });

        Assert.That(correlation.CorrelationId, Is.EqualTo("trusted-server-id"));
    }

    [Test]
    public void WithoutHttpContextGeneratesStableNonEmptyGuidPerInstance()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };

        var first = new HttpCorrelationContext(accessor);
        var firstId = first.CorrelationId;
        var second = new HttpCorrelationContext(accessor);

        Assert.Multiple(() =>
        {
            Assert.That(Guid.TryParse(firstId, out var firstGuid) && firstGuid != Guid.Empty, Is.True);
            Assert.That(Guid.TryParse(second.CorrelationId, out var secondGuid) && secondGuid != Guid.Empty, Is.True);
            Assert.That(first.CorrelationId, Is.EqualTo(firstId));
            Assert.That(second.CorrelationId, Is.Not.EqualTo(firstId));
        });
    }

    [TestCase("Production")]
    public void ModuleRegistersScopedCorrelationWithoutLeakingIdBetweenRequests(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            Args = []
        });
        builder.AddTeam21Acc();

        // Resolve only correlation services: no application or hosted seeder is started.
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ICorrelationContext>());

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { TraceIdentifier = "server-first-request" };
        accessor.HttpContext.Request.Headers[ClientHeader] = "spoofed-first-request";

        ICorrelationContext first;
        using (var firstScope = provider.CreateScope())
        {
            first = firstScope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            var repeated = firstScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<HttpCorrelationContext>());
                Assert.That(repeated, Is.SameAs(first));
                Assert.That(first.CorrelationId, Is.EqualTo("server-first-request"));
            });
        }

        accessor.HttpContext = new DefaultHttpContext { TraceIdentifier = "server-second-request" };
        accessor.HttpContext.Request.Headers[ClientHeader] = "spoofed-second-request";
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.CorrelationId, Is.EqualTo("server-second-request"));
            Assert.That(first.CorrelationId, Is.EqualTo("server-first-request"));
        });
    }
}
