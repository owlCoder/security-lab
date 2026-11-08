using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Api.Modules.Audit;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests.Reading;

/// <summary>DI wiring and route metadata of the AUD-06 additions, without a database.</summary>
[TestFixture]
[Property("RequirementId", "AUD-06")]
public sealed class AuditReadCompositionTests
{
    // Never opened: these tests only build the container.
    private const string UnusedConnectionString = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused";

    private static readonly ServiceProviderOptions StrictOptions = new() { ValidateScopes = true, ValidateOnBuild = true };

    [Test]
    public void ModuleRegistrations_PassStrictValidation_AndResolveTheNewServices()
    {
        using var provider = BuildModule(withRequestContext: true);
        using var scope = provider.CreateScope();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scope.ServiceProvider.GetRequiredService<IAuditCourseReader>(), Is.TypeOf<AuditCourseReader>());
            Assert.That(scope.ServiceProvider.GetRequiredService<IAuditWriter>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetRequiredService<IAuditContextProvider>(), Is.TypeOf<RequestAuditContextProvider>());
        }
    }

    [Test]
    public void WithoutRequestContext_TheWriterKeepsTheFailClosedDefault()
    {
        using var provider = BuildModule(withRequestContext: false);
        using var scope = provider.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<IAuditContextProvider>().GetCurrentContext(), Is.Null);
    }

    [Test]
    public void AddAuditRequestContext_ReplacesTheDefaultProvider_ExactlyOnce()
    {
        var services = ModuleServices(withRequestContext: true);

        var providers = services.Where(d => d.ServiceType == typeof(IAuditContextProvider)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(providers, Has.Count.EqualTo(1));
            Assert.That(providers[0].ImplementationType, Is.EqualTo(typeof(RequestAuditContextProvider)));
            Assert.That(providers[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
        }
    }

    [Test]
    public void RealApiHost_UsesTheSessionActorSource()
    {
        using var factory = new TestingHost();
        using var scope = factory.Services.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<IAuditActorSource>(), Is.TypeOf<SessionAuditActorSource>());
    }

    [Test]
    public void RealApiHost_AuditRoute_IsProtectedByCourseScopedAccess_AndIsReadOnly()
    {
        using var factory = new TestingHost();
        var auditEndpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/audit", StringComparison.OrdinalIgnoreCase) == true
                || endpoint.RoutePattern.RawText?.StartsWith("/api/audit", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.That(auditEndpoints, Has.Count.EqualTo(1), "exactly one audit route: the course view");
        var endpoint = auditEndpoints[0];
        var requirement = endpoint.Metadata.GetMetadata<IAccessRequirementMetadata>();
        var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requirement, Is.Not.Null);
            Assert.That(requirement!.Action, Is.EqualTo("audit.events.read"));
            Assert.That(requirement.ResourceType, Is.EqualTo("course"));
            Assert.That(requirement.IdRouteParam, Is.EqualTo("courseId"));
            Assert.That(endpoint.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null);
            Assert.That(endpoint.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
            Assert.That(methods, Is.EquivalentTo(new[] { "GET" }));
        }
    }

    private static ServiceProvider BuildModule(bool withRequestContext) =>
        ModuleServices(withRequestContext).BuildServiceProvider(StrictOptions);

    private static ServiceCollection ModuleServices(bool withRequestContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAccessControl();
        services.AddAudit();
        services.AddAuditPersistence(UnusedConnectionString);
        if (withRequestContext)
            services.AddAuditRequestContext<SessionAuditActorSource>();
        return services;
    }

    private sealed class TestingHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
