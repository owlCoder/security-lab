using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Audit.Application;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
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
    public void AddCourseAuditView_RegistersTheS07QueryService_WithSafeLifetimes()
    {
        var services = FullComposition();
        using var provider = services.BuildServiceProvider(Strict);
        using var scope = provider.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(services.Single(d => d.ServiceType == typeof(ICourseAuditQueryService)).Lifetime,
                Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(services.Single(d => d.ServiceType == typeof(ICourseAuditEventReader)).Lifetime,
                Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(services.Single(d => d.ServiceType == typeof(CourseAuditQueryNormalizer)).Lifetime,
                Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(scope.ServiceProvider.GetRequiredService<ICourseAuditQueryService>(),
                Is.TypeOf<CourseAuditQueryService>());
            Assert.That(scope.ServiceProvider.GetRequiredService<ICourseAuditEventReader>(),
                Is.TypeOf<PostgresCourseAuditEventReader>());
        });
    }

    [Test]
    public void QueryService_IsNotRegisteredByAuditPersistenceAlone()
    {
        // S-07 has no authorization: without the S-14 endpoint composition it must not be resolvable.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccessControl();
        services.AddAudit();
        services.AddAuditPersistence("Host=127.0.0.1;Port=1;Database=unused;Username=unused");

        Assert.That(services.Any(d => d.ServiceType == typeof(ICourseAuditQueryService)), Is.False);
    }

    [Test]
    public void MapCourseAuditEndpoints_WithoutAddCourseAuditView_FailsAtStartup()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapCourseAuditEndpoints());
        Assert.That(exception!.Message, Does.Contain("AddCourseAuditView"));
    }

    [Test]
    public async Task RealApiHost_MapsTheEndpointOnce_AndAnAnonymousCallerGets401()
    {
        // The real composition from Program.cs, no test overrides: the default subject is anonymous.
        using var factory = new TestingApiHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(CourseAuditEndpointRoute.For(Guid.NewGuid()));
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("audit-events", StringComparison.Ordinal) == true)
            .ToArray();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Has.Length.EqualTo(1));
            Assert.That(endpoints[0].RoutePattern.RawText, Is.EqualTo(CourseAuditEndpointRoute.Pattern));
            Assert.That(endpoints[0].Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action,
                Is.EqualTo("audit.events.read"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(body, Does.Not.Contain("items"));
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
