using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Modules.SessionSecurity;
using Platform.Api.Modules.SessionSecurity.Audit;
using Platform.Audit.Contracts;
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
    private const string PreLoginToken = "pre-login-token-value";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly string UserId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001").ToString("D");

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesRotationUseCase()
    {
        using var provider = BuildProvider(new RecordingTrustedAuditWriter());
        using var scope = provider.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();

        Assert.That(useCase, Is.InstanceOf<SessionRotationService>());
    }

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesLoginHistoryUseCase()
    {
        using var provider = BuildProvider(new RecordingTrustedAuditWriter());
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
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesAuditModuleAdapter()
    {
        using var provider = BuildProvider(new RecordingTrustedAuditWriter());
        using var scope = provider.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<ISessionSecurityAuditWriter>();

        Assert.That(writer, Is.InstanceOf<AuditSessionSecurityAuditWriter>());
    }

    [Test]
    public async Task AddSessionSecurity_WhenAuditModuleIsUnavailable_RotationIsRejected()
    {
        var trusted = new RecordingTrustedAuditWriter(AuditWriteResult.Unavailable());
        using var provider = BuildProvider(trusted);
        using var scope = provider.CreateScope();

        var result = await RotateAsync(scope.ServiceProvider);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
        });
    }

    [Test]
    public async Task AddSessionSecurity_WhenAuditModuleRecords_RotationSucceedsAndIsAudited()
    {
        var trusted = new RecordingTrustedAuditWriter();
        using var provider = BuildProvider(trusted);
        using var scope = provider.CreateScope();

        var result = await RotateAsync(scope.ServiceProvider);

        var (draft, _) = trusted.Writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.SessionRotated));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        });
    }

    private static async Task<SessionRotationResult> RotateAsync(IServiceProvider services)
    {
        var store = (InMemorySessionRotationStore)services.GetRequiredService<ISessionRotationStore>();
        var tokenGenerator = services.GetRequiredService<ISessionTokenGenerator>();
        store.Add(new RotatableSession(
            Guid.NewGuid(),
            SubjectId: null,
            tokenGenerator.ComputeHash(PreLoginToken),
            SessionPrivilegeLevel.PreAuthentication,
            Now.AddMinutes(-5),
            Now.AddHours(8)));

        var useCase = services.GetRequiredService<IRotateSessionUseCase>();
        return await useCase.RotateAsync(new RotateSessionRequest(
            PreLoginToken, UserId, RotationTrigger.Login, SessionPrivilegeLevel.Authenticated, CorrelationId));
    }

    private static ServiceProvider BuildProvider(RecordingTrustedAuditWriter trustedAuditWriter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        // The Audit module (Team 12) is registered by the host; here it is replaced by a recording double.
        services.AddSingleton<ITrustedAuditWriter>(trustedAuditWriter);
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
