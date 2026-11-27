using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests;

[TestFixture]
public sealed class AccessSubjectProviderTests
{
    private sealed class CustomSubjectProvider : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SubjectRef?>(null);
    }

    [Test]
    public async Task DevelopmentFakeSubjectProvider_ReturnsActiveSubjectOnlyInDevelopment()
    {
        var developmentEnvironment = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }).Environment;
        var productionEnvironment = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }).Environment;

        var developmentSubject = await new DevelopmentFakeSubjectProvider(developmentEnvironment)
            .GetSubjectAsync(new DefaultHttpContext());
        var productionSubject = await new DevelopmentFakeSubjectProvider(productionEnvironment)
            .GetSubjectAsync(new DefaultHttpContext());

        Assert.That(developmentSubject, Is.EqualTo(new SubjectRef("dev-user", AccountState.Active)));
        Assert.That(productionSubject, Is.Null);
    }

    [Test]
    public async Task AddAccessAspNet_RegistersAnonymousSubjectProviderByDefault()
    {
        var services = new ServiceCollection();
        services.AddAccessAspNet();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.That(
            scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>(),
            Is.TypeOf<AnonymousAccessSubjectProvider>());
    }

    [Test]
    public async Task AddAccessAspNet_PreservesCustomSubjectProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAccessSubjectProvider, CustomSubjectProvider>();

        services.AddAccessAspNet();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.That(
            scope.ServiceProvider.GetRequiredService<IAccessSubjectProvider>(),
            Is.TypeOf<CustomSubjectProvider>());
    }
}
