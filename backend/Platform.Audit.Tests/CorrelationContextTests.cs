using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Correlation;

namespace Platform.Audit.Tests;

[TestFixture]
public sealed class CorrelationContextTests
{
    private const string GeneratedIdPattern = "^[0-9a-f]{32}$";

    [Test]
    public void Context_BeforeAssign_HasNoCorrelationId()
    {
        var context = new CorrelationContext();

        Assert.That(context.CorrelationId, Is.Null);
    }

    [Test]
    public void Context_AssignedTwice_RejectsSecondValue()
    {
        var context = new CorrelationContext();
        context.Assign("first");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Assign("second"));
            Assert.That(context.CorrelationId, Is.EqualTo("first"));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Context_AssignBlank_IsRejected(string value)
    {
        var context = new CorrelationContext();

        Assert.Throws<ArgumentException>(() => context.Assign(value));
    }

    [Test]
    public void NewId_MatchesAuditContractFormat()
    {
        Assert.That(CorrelationContext.NewId(), Does.Match(GeneratedIdPattern));
    }

    [Test]
    public void AddAudit_RegistersOneCorrelationContextPerScope()
    {
        using var provider = BuildStrictProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var firstPublic = first.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var firstInternal = first.ServiceProvider.GetRequiredService<CorrelationContext>();
        var secondPublic = second.ServiceProvider.GetRequiredService<ICorrelationContext>();

        Assert.Multiple(() =>
        {
            Assert.That(firstPublic, Is.SameAs(firstInternal));
            Assert.That(firstPublic, Is.Not.SameAs(secondPublic));
        });
    }

    [Test]
    public void AddAudit_CorrelationContextFromRootProvider_IsRejected()
    {
        using var provider = BuildStrictProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ICorrelationContext>());
    }

    [Test]
    public async Task Request_GetsServerIdInContextTraceIdentifierAndHeader()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(CorrelationHostFactory.ProbePath);
        var probe = await response.Content.ReadFromJsonAsync<CorrelationProbeResult>();
        var header = response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(probe!.CorrelationId, Does.Match(GeneratedIdPattern));
            Assert.That(probe.TraceIdentifier, Is.EqualTo(probe.CorrelationId));
            Assert.That(header, Is.EqualTo(probe.CorrelationId));
        });
    }

    [Test]
    public async Task Request_WithoutTestSession_IsStillRejectedBySessionCheck()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Cookie");

        using var response = await client.GetAsync(CorrelationHostFactory.ProbePath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("attacker-chosen-id")]
    [TestCase(AuditDraftExamples.CorrelationId)]
    [TestCase("0123456789abcdef0123456789abcdef")]
    public async Task Request_WithClientCorrelationHeader_IgnoresClientValue(string clientValue)
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, CorrelationHostFactory.ProbePath);
        request.Headers.Add(CorrelationMiddleware.HeaderName, clientValue);

        using var response = await client.SendAsync(request);
        var probe = await response.Content.ReadFromJsonAsync<CorrelationProbeResult>();
        var header = response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single();

        Assert.Multiple(() =>
        {
            Assert.That(probe!.CorrelationId, Is.Not.EqualTo(clientValue));
            Assert.That(probe.TraceIdentifier, Is.Not.EqualTo(clientValue));
            Assert.That(header, Is.Not.EqualTo(clientValue));
            Assert.That(probe.CorrelationId, Does.Match(GeneratedIdPattern));
        });
    }

    [Test]
    public async Task TwoRequests_GetDifferentCorrelationIds()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        var first = await client.GetFromJsonAsync<CorrelationProbeResult>(CorrelationHostFactory.ProbePath);
        var second = await client.GetFromJsonAsync<CorrelationProbeResult>(CorrelationHostFactory.ProbePath);

        Assert.That(first!.CorrelationId, Is.Not.EqualTo(second!.CorrelationId));
    }

    [Test]
    public async Task Health_ResponseCarriesGeneratedCorrelationHeader()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single(),
                Does.Match(GeneratedIdPattern));
        });
    }

    [Test]
    public async Task DeniedByAccessControl_ResponseStillCarriesCorrelationHeader()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(CorrelationHostFactory.DeniedProbePath);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(
                response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single(),
                Does.Match(GeneratedIdPattern));
        });
    }

    [Test]
    public async Task Request_CorrelationIdIsAcceptedByAuditValidator()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        var probe = await client.GetFromJsonAsync<CorrelationProbeResult>(CorrelationHostFactory.ProbePath);
        var context = new AuditContext(AuditActor.ForUser(AuditDraftExamples.UserId), probe!.CorrelationId!);
        var draft = new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success);

        Assert.That(new AuditDraftValidator().Validate(draft, context), Is.EqualTo(AuditRejectionCode.None));
    }

    [Test]
    public async Task Request_LogsWrittenDuringRequestCarryCorrelationScope()
    {
        using var factory = new CorrelationHostFactory();
        using var client = factory.CreateClient();

        var probe = await client.GetFromJsonAsync<CorrelationProbeResult>(CorrelationHostFactory.ProbePath);
        var entry = factory.Logs.Entries.Single(e => e.Message == CorrelationProbeController.LogMessage);

        Assert.That(
            entry.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>().SelectMany(scope => scope),
            Has.Some.EqualTo(new KeyValuePair<string, object>(CorrelationMiddleware.LogScopeKey, probe!.CorrelationId!)));
    }

    private static ServiceProvider BuildStrictProvider() =>
        new ServiceCollection()
            .AddAccessControl()
            .AddAudit()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
}

public sealed record CorrelationProbeResult(string? CorrelationId, string TraceIdentifier);

internal sealed class CorrelationHostFactory : WebApplicationFactory<Program>
{
    internal const string ProbePath = "/__test/correlation";
    internal const string DeniedProbePath = "/__test/correlation-denied";

    public ScopeCapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, CorrelationTestSessionManager>();
            services.AddControllers().AddApplicationPart(typeof(CorrelationProbeController).Assembly);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={CorrelationTestSessionManager.Token}");
    }
}

// Only the Testing host receives this synthetic session. The production middleware
// remains active: a client without the known test cookie must still receive 401.
internal sealed class CorrelationTestSessionManager : ISessionManager
{
    internal const string Token = "audit-correlation-test-session";

    public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<ValidateSession?>(token == Token
            ? new ValidateSession(Guid.Parse("99999999-9999-4999-8999-999999999999"),
                AuditDraftExamples.UserId, AuthenticationLevel.Active)
            : null);
    }

    public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessionInfo>>(Array.Empty<SessionInfo>());
}

[ApiController]
public sealed class CorrelationProbeController(ICorrelationContext correlation, ILogger<CorrelationProbeController> logger)
    : ControllerBase
{
    internal const string LogMessage = "correlation probe reached";

    [DelegatedAuthorization]
    [HttpGet(CorrelationHostFactory.ProbePath)]
    public CorrelationProbeResult Get()
    {
        logger.LogInformation(LogMessage);
        return new CorrelationProbeResult(correlation.CorrelationId, HttpContext.TraceIdentifier);
    }

    [HttpGet(CorrelationHostFactory.DeniedProbePath)]
    public CorrelationProbeResult GetWithoutAccessMetadata() =>
        new(correlation.CorrelationId, HttpContext.TraceIdentifier);
}

internal sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<(string Message, IReadOnlyList<object?> Scopes)> Entries { get; } = new();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ScopeCapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<object?>();
            owner._scopes.ForEachScope((scope, list) => list.Add(scope), scopes);
            owner.Entries.Enqueue((formatter(state, exception), scopes));
        }
    }
}
