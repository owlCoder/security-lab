using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Modules.SessionSecurity;
using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class SessionSecurityModuleTests
{
    private const string CorrelationId = "corr-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesRotationUseCase()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();

        Assert.That(useCase, Is.InstanceOf<SessionRotationService>());
    }

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesLoginHistoryUseCase()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<ISearchLoginHistoryUseCase>();

        Assert.That(useCase, Is.InstanceOf<LoginHistoryService>());
    }

    [Test]
    public void ApiHost_WhenStarted_ResolvesSessionSecurityUseCases()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();

        var rotation = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();
        var loginHistory = scope.ServiceProvider.GetRequiredService<ISearchLoginHistoryUseCase>();

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.InstanceOf<SessionRotationService>());
            Assert.That(loginHistory, Is.InstanceOf<LoginHistoryService>());
        });
    }

    [Test]
    public async Task UnavailableSessionSecurityAuditWriter_TryWriteAsync_ReturnsFalse()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISessionSecurityAuditWriter>();

        var result = await writer.TryWriteAsync(new SessionSecurityAuditEntry(
            SessionSecurityAuditEvents.SessionRotated, "user-a", RotationTrigger.Login,
            SessionRotationCodes.Rotated, Now, CorrelationId));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AddSessionSecurity_WithUnavailableAudit_RotationIsRejected()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        const string preLoginToken = "pre-login-token-value";

        var store = (InMemorySessionRotationStore)scope.ServiceProvider
            .GetRequiredService<ISessionRotationStore>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISessionTokenGenerator>();
        store.Add(new RotatableSession(
            Guid.NewGuid(),
            SubjectId: null,
            tokenGenerator.ComputeHash(preLoginToken),
            SessionPrivilegeLevel.PreAuthentication,
            Now.AddMinutes(-5),
            Now.AddHours(8)));

        var useCase = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();
        var result = await useCase.RotateAsync(new RotateSessionRequest(
            preLoginToken, "user-a", RotationTrigger.Login, SessionPrivilegeLevel.Authenticated, CorrelationId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
        });
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSessionSecurity();
        // These tests exercise the module's wiring in isolation, not a real store adapter.
        // AddSessionSecuritySessions() registers the same in-memory store for now; the line
        // prepares for IdentitySessionRotationStore, which Platform.Identity.Tests/SessionSecurity
        // will cover over PostgreSQL once it exists.
        services.Replace(ServiceDescriptor.Singleton<ISessionRotationStore, InMemorySessionRotationStore>());
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
