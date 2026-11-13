using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Composition;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Modules.Team21.Integration;
using Platform.Api.Services.Identity;
using Team21.Acc.Api;
using Team21.Acc.Api.Access;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Tests.Api;

public class Team21SessionAccessPipelineTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ProtectedValue = "protected-test-resource";
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly List<string> _order = [];
    private TestSessions _sessions = null!;
    private TestAccounts _accounts = null!;

    [SetUp]
    public async Task SetUp()
    {
        _order.Clear();
        _sessions = new TestSessions(_order);
        _accounts = new TestAccounts();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAccessControl();
        builder.Services.AddAccessAspNet();
        builder.Services.AddTeam21AccessPolicies();
        builder.Services.AddScoped<IIdentityContext, SessionIdentityContext>();
        builder.Services.AddScoped<ICurrentActor, SessionCurrentActor>();
        builder.Services.AddSingleton<ISessionManager>(_sessions);
        builder.Services.AddSingleton<IAccountStatusReader>(_accounts);
        builder.Services.AddScoped<IAccessSubjectProvider>(_ => new OriginalSubject(_order));
        builder.Services.AddTeam21SessionAccess();
        _app = builder.Build();
        _app.UseRouting();
        _app.UseTeam21SessionBeforeAccess();
        _app.UseAccessControl();
        _app.UseSessionForOtherEndpoints();
        _app.MapGroup("/team21").WithMetadata(new Team21SessionRequiredMetadata())
            .MapGet("", (ICurrentActor actor) => Results.Ok(new { actor.UserId, Resource = ProtectedValue }))
            .RequireAccess(AccessActions.GroupRead.Id);
        _app.MapGet("/other", () => Results.Ok("other"))
            .RequireAccess(AccessActions.GroupRead.Id);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    [TestCase("valid", HttpStatusCode.OK, 1)]
    [TestCase(null, HttpStatusCode.Unauthorized, 0)]
    [TestCase("invalid", HttpStatusCode.Unauthorized, 1)]
    [TestCase("expired", HttpStatusCode.Unauthorized, 1)]
    [TestCase("revoked", HttpStatusCode.Unauthorized, 1)]
    [TestCase("mfa", HttpStatusCode.Unauthorized, 1)]
    [TestCase("empty-id", HttpStatusCode.Unauthorized, 1)]
    public async Task SessionIsValidatedOnceAndHeadersCannotSupplyIdentity(string? token, HttpStatusCode expected, int calls)
    {
        using var response = await Send("/team21", token);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(_sessions.Calls, Is.EqualTo(calls));
        Assert.That(_order, Does.Not.Contain("inner"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, expected == HttpStatusCode.OK ? Does.Contain(UserId.ToString()) : Does.Not.Contain(ProtectedValue));
        Assert.That(body, Does.Not.Contain("client-selected-identity"));
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Inactive)]
    [TestCase((AccountStatus)99)]
    public async Task AccountTransitionsFollowTheExistingAccess403Contract(AccountStatus denied)
    {
        using var active = await Send("/team21", "valid");
        Assert.That(active.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        _accounts.Status = denied;
        using var forbidden = await Send("/team21", "valid");
        Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var body = await forbidden.Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain(ProtectedValue).And.Not.Contain(UserId.ToString()));
        _accounts.Status = AccountStatus.Active;
        using var restored = await Send("/team21", "valid");
        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_sessions.Calls, Is.EqualTo(3));
    }

    [Test]
    public async Task AccountReaderFailureDoesNotExposeTheResource()
    {
        _accounts.Throw = true;
        using var response = await Send("/team21", "valid");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain(ProtectedValue));
        Assert.That(_sessions.Calls, Is.EqualTo(1));
    }

    [TestCase("valid", HttpStatusCode.OK)]
    [TestCase(null, HttpStatusCode.Unauthorized)]
    public async Task OtherEndpointsRetainAccessBeforeSession(string? token, HttpStatusCode expected)
    {
        using var response = await Send("/other", token);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(_order, Is.EqualTo(token is null ? new[] { "inner" } : new[] { "inner", "session" }));
    }

    [Test]
    public async Task IdentityDoesNotSurviveTheNextAnonymousRequest()
    {
        using var first = await Send("/team21", "valid");
        using var second = await Send("/team21", null);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_sessions.Calls, Is.EqualTo(1));
        Assert.That(await second.Content.ReadAsStringAsync(), Does.Not.Contain(UserId.ToString()));
    }

    [Test]
    public void RealTestingHostMarksOnlyTheTwelveTeam21RequireAccessRoutes()
    {
        // Testing starts no Development seeder and applies no startup migrations.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });
        var routes = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().ToList();
        var marked = routes.Where(e => e.Metadata.GetMetadata<Team21SessionRequiredMetadata>() is not null).ToList();
        Assert.That(marked, Has.Count.EqualTo(12));
        foreach (var route in marked)
        {
            Assert.That(route.RoutePattern.RawText, Does.StartWith("/api/courses").Or.StartWith("/api/materials"));
            Assert.That(route.Metadata.GetMetadata<IAccessRequirementMetadata>(), Is.Not.Null);
            Assert.That(route.Metadata.GetMetadata<DelegatedAuthorizationAttribute>(), Is.Null);
            Assert.That(route.Metadata.GetMetadata<PublicEndpointMetadata>(), Is.Null);
            Assert.That(route.Metadata.GetMetadata<AllowPublicAttribute>(), Is.Null);
        }
        Assert.That(routes.Where(e => e.RoutePattern.RawText?.StartsWith("/api/revisions") == true)
            .All(e => e.Metadata.GetMetadata<Team21SessionRequiredMetadata>() is null), Is.True);
    }

    private async Task<HttpResponseMessage> Send(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        request.Headers.Add("X-Test-User", "client-selected-identity");
        request.Headers.Add("X-Stub-User", Guid.NewGuid().ToString());
        return await _client.SendAsync(request);
    }

    // Controlled contract-level validation, not a replacement production authenticator.
    // The existing full-host tests separately retain the real IDN SessionManager.
    private sealed class TestSessions(List<string> order) : ISessionManager
    {
        public int Calls { get; private set; }
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
        {
            Calls++;
            order.Add("session");
            return Task.FromResult<ValidateSession?>(token switch
            {
                "valid" => new ValidateSession(Guid.NewGuid(), UserId, AuthenticationLevel.Active),
                "mfa" => new ValidateSession(Guid.NewGuid(), UserId, AuthenticationLevel.MfaPending),
                "empty-id" => new ValidateSession(Guid.NewGuid(), Guid.Empty, AuthenticationLevel.Active),
                _ => null
            });
        }
        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TestAccounts : IAccountStatusReader
    {
        public AccountStatus Status { get; set; } = AccountStatus.Active;
        public bool Throw { get; set; }
        public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
            Throw ? throw new InvalidOperationException("Unavailable") : Task.FromResult(Status);
    }

    private sealed class OriginalSubject(List<string> order) : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            order.Add("inner");
            return ValueTask.FromResult<SubjectRef?>(new SubjectRef("other-module", AccountState.Active));
        }
    }
}
