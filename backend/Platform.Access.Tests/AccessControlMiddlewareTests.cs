using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Application.Options;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.AspNet;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class AccessControlMiddlewareTests
{
    private sealed record CapturedLog(LogLevel Level, EventId EventId, string Message);

    private sealed class CaptureLoggerProvider(List<CapturedLog> entries) : ILoggerProvider
    {
        private sealed class CaptureLogger(List<CapturedLog> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                logs.Add(new CapturedLog(logLevel, eventId, formatter(state, exception)));
        }

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(entries);
        public void Dispose() { }
    }

    private sealed class FakeDecision : IAccessDecision
    {
        public Func<AccessRequest, CancellationToken, ValueTask<AccessDecision?>> Handler { get; set; } =
            (r, _) => new(AccessDecision.Deny(AccessDenialReason.NoPolicy, r.CorrelationId));

        public List<AccessRequest> Calls { get; } = new();

        public async ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return (await Handler(request, cancellationToken))!;   
        }
    }

    private sealed class FakeSubjects : IAccessSubjectProvider
    {
        public SubjectRef? Subject { get; set; } = new("user-1", AccountState.Active);
        public bool Throw { get; set; }

        public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext httpContext, CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("boom") : new(Subject);
    }

    private sealed class FakeAuditWriter : IAccessAuditWriter
    {
        public List<(AccessRequest Request, AccessDecision Decision)> Writes { get; } = new();
        public bool BlockUntilCancelled { get; set; }
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WriteAsync(
            AccessRequest request,
            AccessDecision decision,
            CancellationToken cancellationToken = default)
        {
            Writes.Add((request, decision));
            WriteStarted.TrySetResult();
            if (!BlockUntilCancelled)
            {
                return;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private FakeDecision _decision = null!;
    private FakeSubjects _subjects = null!;
    private FakeAuditWriter _audit = null!;
    private List<CapturedLog> _logs = null!;
    private int _handlerCalls;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private CancellationTokenSource _requestAborted = null!;

    private static ValueTask<AccessDecision?> Allow(AccessRequest r) =>
        new(AccessDecision.Allow("test-policy", r.CorrelationId));

    [SetUp]
    public async Task SetUp()
    {
        _decision = new FakeDecision();
        _subjects = new FakeSubjects();
        _audit = new FakeAuditWriter();
        _logs = new List<CapturedLog>();
        _handlerCalls = 0;
        _requestAborted = new CancellationTokenSource();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(new CaptureLoggerProvider(_logs));
        builder.Services.AddSingleton(new AccessDecisionOptions { AuditTimeout = TimeSpan.FromMilliseconds(100) });
        builder.Services.AddSingleton<IAccessDecision>(_decision);
        builder.Services.AddSingleton<IAccessSubjectProvider>(_subjects);
        builder.Services.AddSingleton<IAccessAuditWriter>(_audit);

        _app = builder.Build();
        _app.UseRouting();
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/client-abort")
            {
                context.RequestAborted = _requestAborted.Token;
            }

            await next();
        });
        _app.UseAccessControl();

        IResult Handler() { _handlerCalls++; return Results.Ok("handler"); }
        IResult HandlerId(string id) { _handlerCalls++; return Results.Ok(id); }

        _app.MapGet("/public", Handler).RequireAccess(AccessActions.PublicInfoRead.Id);
        _app.MapGet("/forgotten", Handler);                                   // no metadata
        _app.MapGet("/client-abort", Handler).RequireAccess("public.info.read");
        _app.MapGet("/unknown-action", Handler).RequireAccess("unknown.action");
        _app.MapGet("/duplicate", Handler)
            .RequireAccess("public.info.read")
            .RequireAccess("public.info.read");
        _app.MapGet("/labs/{id}", HandlerId).RequireAccess("lab.read", "laboratory", "id");
        _app.MapGet("/noresource", Handler).RequireAccess("public.info.read");

        var group = _app.MapGroup("/grp").RequireAccess("public.info.read");  // generic extension on a group
        group.MapGet("/x", Handler);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _requestAborted.Dispose();
    }

    private static string Id() => Guid.NewGuid().ToString("D");

    [Test]
    public async Task ACC05_Allow_reaches_handler_with_validated_request()
    {
        _decision.Handler = (r, _) => Allow(r);
        var id = Id();

        var res = await _client.GetAsync($"/labs/{id}");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_handlerCalls, Is.EqualTo(1));
        var call = _decision.Calls.Single();
        Assert.That(call.Action, Is.EqualTo("lab.read"));
        Assert.That(call.Resource, Is.EqualTo(new ResourceRef("laboratory", id)));
        Assert.That(call.Subject, Is.EqualTo(new SubjectRef("user-1", AccountState.Active)));
    }

    [Test]
    public async Task ACC05_Public_action_uses_access_middleware()
    {
        _decision.Handler = (r, _) => Allow(r);

        var res = await _client.GetAsync("/public");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_handlerCalls, Is.EqualTo(1));
        Assert.That(_decision.Calls.Single().Action, Is.EqualTo(AccessActions.PublicInfoRead.Id));
    }

    [Test]
    public async Task ACC05_Group_level_RequireAccess_is_enforced()
    {
        var res = await _client.GetAsync("/grp/x");   // default fake decision denies
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
        Assert.That(_decision.Calls.Single().Action, Is.EqualTo("public.info.read"));
    }

    [Test]
    public async Task ACC05_Deny_does_not_call_handler_and_returns_generic_403()
    {
        var id = Id();
        _decision.Handler = (r, _) => new(AccessDecision.Deny(AccessDenialReason.NoPolicy, r.CorrelationId));

        var res = await _client.GetAsync($"/labs/{id}");
        var body = await res.Content.ReadAsStringAsync();

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(res.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(res.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(_handlerCalls, Is.Zero);

        using var doc = JsonDocument.Parse(body);
        Assert.That(doc.RootElement.GetProperty("title").GetString(), Is.EqualTo("Forbidden"));
        Assert.That(doc.RootElement.GetProperty("correlationId").GetString(), Is.Not.Empty);

        Assert.That(body, Does.Not.Contain("NoPolicy").IgnoreCase
            .And.Not.Contain("policy").IgnoreCase
            .And.Not.Contain("lab.read")
            .And.Not.Contain("laboratory")
            .And.Not.Contain(id)
            .And.Not.Contain("/labs"));
    }

    [Test]
    public async Task ACC05_Anonymous_subject_gets_401()
    {
        _subjects.Subject = null;
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_handlerCalls, Is.Zero);
    }

    [Test]
    public async Task ACC05_Unauthenticated_denial_reason_gives_401()
    {
        _decision.Handler = (r, _) => new(AccessDecision.Deny(AccessDenialReason.Unauthenticated, r.CorrelationId));
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ACC05_Route_without_metadata_is_fallback_denied()
    {
        var res = await _client.GetAsync("/forgotten");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
        Assert.That(_decision.Calls, Is.Empty);
    }

    [Test]
    public async Task ACC05_Audit_writer_timeout_does_not_delay_403()
    {
        _audit.BlockUntilCancelled = true;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var response = await _client.GetAsync("/forgotten").WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await _audit.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(_audit.Writes, Has.Count.EqualTo(1));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        var alarm = _logs.Single(log => log.EventId == AccessAuditEventIds.Lost);
        Assert.That(alarm.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(alarm.Message, Does.Contain("Status: timeout"));
    }

    [Test]
    public async Task ACC05_RequestAbortedDoesNotCancelAuditBeforeIndependentTimeout()
    {
        _audit.BlockUntilCancelled = true;
        _decision.Handler = (_, _) => throw new InvalidOperationException("decision failure");
        var request = _client.GetAsync("/client-abort");

        await _audit.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _requestAborted.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(25));

        Assert.That(_audit.CancellationObserved.Task.IsCompleted, Is.False);
        await _audit.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(60)));
        Assert.That(_logs.Count(log => log.EventId == AccessAuditEventIds.Lost), Is.EqualTo(1));

        try
        {
            using var response = await request;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
        catch (OperationCanceledException)
        {
            // A disconnected request cannot receive the 403, but its audit write must run to timeout.
        }
        catch (HttpRequestException)
        {
            // TestServer may surface the explicitly cancelled RequestAborted token as a disconnected response.
        }
    }

    [Test]
    public async Task ACC05_Unknown_action_without_policy_is_denied()
    {
        var res = await _client.GetAsync("/unknown-action");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
        Assert.That(_decision.Calls.Single().Action, Is.EqualTo("unknown.action"));
    }

    [Test]
    public async Task ACC05_Multiple_requirements_are_denied_and_audited()
    {
        _decision.Handler = (r, _) => Allow(r);

        var res = await _client.GetAsync("/duplicate");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
        Assert.That(_decision.Calls, Is.Empty);
        Assert.That(_audit.Writes.Single().Decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_Null_decision_is_denied()
    {
        _decision.Handler = (_, _) => new((AccessDecision?)null);
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
    }

    [Test]
    public async Task ACC05_Unknown_outcome_value_is_denied()
    {
        _decision.Handler = (r, _) => new(new AccessDecision((AccessOutcome)99, AccessDenialReason.None, "p", "v1", r.CorrelationId));
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
    }

    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("1%27%20OR%201%3D1")]
    [TestCase("abc%0d%0aInjected-Log-Line")]
    public async Task ACC05_Invalid_resource_id_is_denied_before_engine(string raw)
    {
        _decision.Handler = (r, _) => Allow(r);
        var res = await _client.GetAsync($"/labs/{raw}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_decision.Calls, Is.Empty);
        Assert.That(_handlerCalls, Is.Zero);
    }

    [Test]
    public async Task ACC05_Overlong_resource_id_is_denied_before_engine()
    {
        _decision.Handler = (r, _) => Allow(r);
        var res = await _client.GetAsync("/labs/" + new string('a', 5000));
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_decision.Calls, Is.Empty);
    }

    [Test]
    public async Task ACC05_Resource_id_is_normalized_to_canonical_form()
    {
        _decision.Handler = (r, _) => Allow(r);
        var id = Guid.NewGuid();
        await _client.GetAsync($"/labs/{id:N}");   // 32 hex digits, no dashes
        Assert.That(_decision.Calls.Single().Resource!.Id, Is.EqualTo(id.ToString("D")));
    }

    [Test]
    public async Task ACC05_Engine_exception_is_denied()
    {
        _decision.Handler = (_, _) => throw new InvalidOperationException("engine down");
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
    }

    [Test]
    public async Task ACC05_Engine_timeout_is_denied_not_propagated()
    {
        _decision.Handler = (_, _) => throw new OperationCanceledException();   // our own CTS fired, client still connected
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_handlerCalls, Is.Zero);
    }

    [Test]
    public async Task ACC05_Subject_provider_failure_is_denied()
    {
        _subjects.Throw = true;
        _decision.Handler = (r, _) => Allow(r);
        var res = await _client.GetAsync($"/labs/{Id()}");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_decision.Calls, Is.Empty);
    }

    [Test]
    public async Task ACC05_Unknown_route_is_404_and_nothing_runs()
    {
        var res = await _client.GetAsync("/does-not-exist");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(_handlerCalls, Is.Zero);
        Assert.That(_decision.Calls, Is.Empty);
    }

    [Test]
    public async Task ACC05_Client_supplied_identity_role_course_and_correlation_are_ignored()
    {
        _decision.Handler = (r, _) => Allow(r);
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"/labs/{Id()}?role=admin&courseId={Guid.NewGuid()}&userId=attacker");
        req.Headers.Add("X-Role", "Administrator");
        req.Headers.Add("X-Stub-User", "attacker");
        req.Headers.Add("X-Correlation-Id", "client-chosen");

        await _client.SendAsync(req);

        var call = _decision.Calls.Single();
        Assert.That(call.Subject, Is.EqualTo(new SubjectRef("user-1", AccountState.Active)));
        Assert.That(call.CorrelationId, Is.Not.EqualTo("client-chosen"));
    }

    [Test]
    public async Task ACC05_Response_correlation_id_is_server_generated()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/labs/{Id()}");
        req.Headers.Add("X-Correlation-Id", "client-chosen");
        var body = await (await _client.SendAsync(req)).Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain("client-chosen"));
    }

    [Test]
    public void ACC05_RequireAccess_rejects_invalid_registrations()
    {
        var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentException>(() => app.MapGet("/a", () => "x").RequireAccess("Lab.Read"));
        Assert.Throws<ArgumentException>(() => app.MapGet("/b", () => "x").RequireAccess("lab.read", "laboratory", null));
        Assert.Throws<ArgumentException>(() => app.MapGet("/c", () => "x").RequireAccess("lab.read", "Lab Type", "id"));
    }
}
