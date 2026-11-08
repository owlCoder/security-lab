using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Composition;
using Platform.Audit.Endpoints;

namespace Platform.Audit.Tests.CourseView;

[TestFixture]
public sealed class CourseAuditViewCompositionTests
{
    private static readonly ServiceProviderOptions Strict = new() { ValidateScopes = true, ValidateOnBuild = true };

    private static ServiceCollection FullComposition()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccessControl();
        services.AddAudit();
        // Not opened at build time; no database is contacted.
        services.AddAuditPersistence("Host=127.0.0.1;Port=1;Database=unused;Username=unused");
        services.AddCourseAuditView();
        return services;
    }

    [Test]
    public void AddCourseAuditView_BuildsUnderStrictValidation_WithMatchingLifetimes()
    {
        var services = FullComposition();
        using var provider = services.BuildServiceProvider(Strict);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(services.Single(d => d.ServiceType == typeof(CourseAuditReadAuthorizer)).Lifetime,
                Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(services.Single(d => d.ServiceType == typeof(CourseAuditViewRecorder)).Lifetime,
                Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(first.ServiceProvider.GetRequiredService<CourseAuditReadAuthorizer>(),
                Is.SameAs(second.ServiceProvider.GetRequiredService<CourseAuditReadAuthorizer>()));
            Assert.That(first.ServiceProvider.GetRequiredService<CourseAuditViewRecorder>(),
                Is.Not.SameAs(second.ServiceProvider.GetRequiredService<CourseAuditViewRecorder>()));
        });
    }

    [Test]
    public void RecorderFromTheRootProvider_IsRefused()
    {
        // A scoped writer captured by a singleton would mix requests (captive dependency).
        using var provider = FullComposition().BuildServiceProvider(Strict);

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<CourseAuditViewRecorder>());
    }

    [Test]
    public void CallingTwice_DoesNotDuplicateRegistrations()
    {
        var services = FullComposition();
        services.AddCourseAuditView();

        Assert.Multiple(() =>
        {
            Assert.That(services.Count(d => d.ServiceType == typeof(CourseAuditReadAuthorizer)), Is.EqualTo(1));
            Assert.That(services.Count(d => d.ServiceType == typeof(CourseAuditViewRecorder)), Is.EqualTo(1));
        });
    }

    [Test]
    public void WithoutAuditPersistence_StartupFails()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccessControl();
        services.AddAudit();
        services.AddCourseAuditView();

        Assert.Catch<Exception>(() => services.BuildServiceProvider(Strict).Dispose());
    }

    [Test]
    public async Task RealApiHost_DoesNotExposeTheUnfinishedEndpoint()
    {
        // Until the S-07 query service is merged and the handler is wired, the route stays unmapped.
        // When S-14 maps it, this test is replaced by the endpoint's own authorization tests.
        using var factory = new TestingApiHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(CourseAuditEndpointRoute.For(Guid.NewGuid()));
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(endpoint => endpoint.RoutePattern.RawText?.Contains("audit-events", StringComparison.Ordinal) == true);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(mapped, Is.False);
        });
    }

    private sealed class TestingApiHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
