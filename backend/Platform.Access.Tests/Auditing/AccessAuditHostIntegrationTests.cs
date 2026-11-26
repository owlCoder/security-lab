using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.AccessAudit;
using Platform.Audit.Contracts;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class AccessAuditHostIntegrationTests
{
    private sealed class RecordingTrustedAuditWriter : ITrustedAuditWriter
    {
        public AuditWriteResult Result { get; set; } = AuditWriteResult.Unavailable();

        public List<(AuditEventDraft Draft, AuditContext Context)> Writes { get; } = [];

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            Writes.Add((draft, context));
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class AuditingDenyDecision(IAccessAuditWriter auditWriter) : IAccessDecision
    {
        public async ValueTask<AccessDecision> DecideAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default)
        {
            var decision = AccessDecision.Deny(
                AccessDenialReason.PolicyDenied,
                request.CorrelationId);
            await auditWriter.WriteAsync(request, decision, cancellationToken);
            return decision;
        }
    }

    private sealed class FixedSubjectProvider : IAccessSubjectProvider
    {
        public ValueTask<SubjectRef?> GetSubjectAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SubjectRef?>(new SubjectRef(
                "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
                AccountState.Active));
    }

    private sealed record CapturedLog(LogLevel Level, EventId EventId, string Message);

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<CapturedLog> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger(List<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add(new CapturedLog(logLevel, eventId, formatter(state, exception)));
    }

    [Test]
    public void PlatformApi_RegistersCentralAccessAuditWriter()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        Assert.That(
            factory.Services.GetRequiredService<IAccessAuditWriter>(),
            Is.TypeOf<CentralAccessAuditWriter>());
    }

    [Test]
    public async Task PlatformApi_AuditOmitsCrLfAndLogsOnlySafeFailureFields()
    {
        const string injectedText = "forged\r\nInjected-Log-Line";
        var trustedWriter = new RecordingTrustedAuditWriter
        {
            Result = AuditWriteResult.Unavailable()
        };
        var logs = new CaptureLoggerProvider();
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITrustedAuditWriter>();
                    services.AddSingleton<ITrustedAuditWriter>(trustedWriter);
                    services.AddLogging(logging => logging.AddProvider(logs));
                });
            });

        var writer = factory.Services.GetRequiredService<IAccessAuditWriter>();
        var decision = AccessDecision.Deny(
            AccessDenialReason.PolicyDenied,
            "engine-correlation");
        var userId = Guid.NewGuid().ToString("D");

        await writer.WriteAsync(
            new AccessRequest(
                new SubjectRef(userId, AccountState.Active),
                $"course.read\r\n{injectedText}",
                null,
                "request-correlation"),
            decision);

        Assert.That(trustedWriter.Writes, Has.Count.EqualTo(1));
        var attempted = trustedWriter.Writes.Single();
        Assert.That(attempted.Context.Actor, Is.EqualTo(AuditActor.ForUser(Guid.Parse(userId))));
        Assert.That(attempted.Context.CorrelationId, Is.EqualTo("engine-correlation"));
        Assert.That(
            string.Join(" ", attempted.Draft.Details!.Values),
            Does.Not.Contain("forged").And.Not.Contain("Injected-Log-Line"));

        await writer.WriteAsync(
            new AccessRequest(
                new SubjectRef($"user\r\n{injectedText}", AccountState.Active),
                "course.read",
                null,
                "request-correlation"),
            decision);

        Assert.That(trustedWriter.Writes, Has.Count.EqualTo(1));
        var alerts = logs.Entries
            .Where(entry => entry.EventId == AccessAuditEventIds.Lost)
            .ToArray();
        Assert.That(alerts, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(alerts[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(alerts[0].Message, Does.Contain("access.denied"));
            Assert.That(alerts[0].Message, Does.Contain("Unavailable"));
            Assert.That(alerts[0].Message, Does.Contain("engine-correlation"));
            Assert.That(alerts[1].Message, Does.Contain("invalid_subject_id"));
            Assert.That(alerts.All(entry =>
                !entry.Message.Contains("forged", StringComparison.Ordinal)
                && !entry.Message.Contains("Injected-Log-Line", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task PlatformApi_UnavailableAuditStillReturns403AndLogsAlarm()
    {
        var trustedWriter = new RecordingTrustedAuditWriter
        {
            Result = AuditWriteResult.Unavailable()
        };
        var logs = new CaptureLoggerProvider();
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITrustedAuditWriter>();
                    services.AddSingleton<ITrustedAuditWriter>(trustedWriter);
                    services.RemoveAll<IAccessSubjectProvider>();
                    services.AddSingleton<IAccessSubjectProvider, FixedSubjectProvider>();
                    services.RemoveAll<IAccessDecision>();
                    services.AddSingleton<IAccessDecision>(provider =>
                        new AuditingDenyDecision(provider.GetRequiredService<IAccessAuditWriter>()));
                    services.AddLogging(logging => logging.AddProvider(logs));
                });
            });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
        Assert.That(trustedWriter.Writes, Has.Count.EqualTo(1));
        var alert = logs.Entries.Single(entry => entry.EventId == AccessAuditEventIds.Lost);
        Assert.Multiple(() =>
        {
            Assert.That(alert.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(alert.Message, Does.Contain("Status: Unavailable"));
            Assert.That(alert.Message, Does.Contain("EventType: access.denied"));
        });
    }
}
