using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Audit.Contracts;

namespace Platform.Tests.RevisionAuthorization;

public sealed class RevisionAuditForwardingTests
{
    [Test]
    public async Task Registration_ForwardsDeniedEventToScopedWriter_AndKeepsAccessAudit()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAccessControl();
        var inner = new RecordingAccessWriter();
        collection.AddSingleton<IAccessAuditWriter>(inner);
        collection.AddRevisionAuditForwarding();
        collection.AddRevisionAuditForwarding();
        var audit = new RecordingTrustedWriter();
        collection.AddScoped<ITrustedAuditWriter>(_ => audit);
        collection.AddScoped<IRevisionAuditContextProvider>(_ => new Context());
        using var services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });

        var writer = services.GetRequiredService<IAccessAuditWriter>();
        await writer.WriteAsync(Request(), Denial());

        Assert.Multiple(() =>
        {
            Assert.That(inner.Count, Is.EqualTo(1));
            Assert.That(audit.Count, Is.EqualTo(1));
            Assert.That(audit.LastContext, Is.EqualTo(new Context().GetContext()));
            Assert.That(audit.LastDraft!.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
        });
    }

    [TestCase("unavailable")]
    [TestCase("rejected")]
    [TestCase("throws")]
    [TestCase("missing")]
    public async Task AuditFailure_LogsAlarm_AndDoesNotChangeAccessDecision(string failure)
    {
        var logger = new RecordingLogger();
        var collection = new ServiceCollection();
        collection.AddScoped<IRevisionAuditContextProvider>(_ => new Context());
        if (failure != "missing")
            collection.AddScoped<ITrustedAuditWriter>(_ => new RecordingTrustedWriter(failure));
        using var services = collection.BuildServiceProvider();
        var writer = new RevisionAuditForwarder(new RecordingAccessWriter(),
            services.GetRequiredService<IServiceScopeFactory>(), logger);

        await writer.WriteAsync(Request(), Denial());
        Assert.That(logger.Errors, Is.EqualTo(1));
    }

    [TestCase(true, RevisionActions.Read)]
    [TestCase(false, "laboratory.read")]
    public async Task AllowAndOtherModules_OnlyUseOriginalAccessWriter(bool allowed, string action)
    {
        var collection = new ServiceCollection();
        var audit = new RecordingTrustedWriter();
        collection.AddScoped<ITrustedAuditWriter>(_ => audit);
        using var services = collection.BuildServiceProvider();
        var inner = new RecordingAccessWriter();
        var writer = new RevisionAuditForwarder(inner, services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RevisionAuditForwarder>.Instance);

        await writer.WriteAsync(Request() with { Action = action }, allowed
            ? AccessDecision.Allow("test", "test") : Denial());
        Assert.That(inner.Count, Is.EqualTo(1));
        Assert.That(audit.Count, Is.Zero);
    }

    [Test]
    public void Context_IgnoresClientHeadersAndUsesValidatedSession()
    {
        var user = Guid.NewGuid();
        var http = new DefaultHttpContext { TraceIdentifier = "server-correlation" };
        http.Request.Headers["X-Actor-ID"] = Guid.NewGuid().ToString();
        http.Request.Headers["X-Correlation-ID"] = "client-correlation";
        http.Items[SessionCheckMiddleware.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), user, AuthenticationLevel.Active);
        var provider = new RevisionAuditContextProvider(new HttpContextAccessor { HttpContext = http });
        Assert.That(provider.GetContext(), Is.EqualTo(new AuditContext(AuditActor.ForUser(user), "server-correlation")));
    }

    [Test]
    public void Context_WithoutConfirmedActiveSession_IsAnonymous()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "server-correlation" };
        http.Items[SessionCheckMiddleware.SessionItemKey] =
            new ValidateSession(Guid.NewGuid(), Guid.NewGuid(), AuthenticationLevel.MfaPending);
        var provider = new RevisionAuditContextProvider(new HttpContextAccessor { HttpContext = http });
        Assert.That(provider.GetContext().Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    [TestCase(RevisionActions.Create)]
    [TestCase(RevisionActions.Read)]
    [TestCase(RevisionActions.Mine)]
    [TestCase(RevisionActions.PendingReviews)]
    public void HelperActionDenial_AlsoProducesAuditEvent(string action) =>
        Assert.That(RevisionAuditEventFactory.CreateDeniedEvent(Request() with { Action = action }, Denial()), Is.Not.Null);

    private static AccessRequest Request() => new(new SubjectRef(Guid.NewGuid().ToString(), AccountState.Active),
        RevisionActions.Read, new ResourceRef("revision", Guid.NewGuid().ToString()), "test");
    private static AccessDecision Denial() => AccessDecision.Deny(AccessDenialReason.PolicyDenied, "test");

    private sealed class Context : IRevisionAuditContextProvider
    {
        public AuditContext GetContext() => new(AuditActor.Anonymous, "server-correlation");
    }

    private sealed class RecordingAccessWriter : IAccessAuditWriter
    {
        public int Count { get; private set; }
        public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingTrustedWriter(string? failure = null) : ITrustedAuditWriter
    {
        public int Count { get; private set; }
        public AuditContext? LastContext { get; private set; }
        public AuditEventDraft? LastDraft { get; private set; }
        public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Count++;
            LastContext = context;
            LastDraft = draft;
            return ValueTask.FromResult(failure switch
            {
                "throws" => throw new InvalidOperationException("must not be logged"),
                "unavailable" => AuditWriteResult.Unavailable(),
                "rejected" => AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext),
                _ => AuditWriteResult.Recorded(Guid.NewGuid())
            });
        }
    }

    private sealed class RecordingLogger : ILogger<RevisionAuditForwarder>
    {
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error) Errors++;
        }
    }
}
