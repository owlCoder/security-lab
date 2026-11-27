using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

// ACC-05 / #197: what the central Audit path guarantees for access denials now that Access no
// longer aggregates or buffers them (DECISIONS.md D2, D6). The requests go through the real
// Platform.Api host: Access middleware, decision engine, CentralAccessAuditWriter and AuditWriter.
// The probe route requires audit.events.read on a course; the synthetic teacher is a member of
// the probe course only, so any other course is denied by the real S-08 policy.

[TestFixture]
[Category("AuditRuntime")]
[Property("RequirementId", "ACC-05")]
public sealed class AccessDenialAuditPostgresTests
{
    private const int Denials = 100;
    private const int Parallelism = 10;

    // These tests check ordering and completeness, not the latency budget. Since f84d222 the
    // engine cancels the audit write when AccessDecisionOptions.AuditTimeout (1 s) expires. The
    // first write in a fresh host also builds the EF model and opens a new connection (pooling is
    // off here), which on a CI runner can take longer than 1 s. That denial is then correctly
    // returned with an access.audit.lost alarm and no record, and the test fails for a reason it
    // does not test. The 1 s budget itself is covered by DefaultDenyEngineTests and
    // AccessControlMiddlewareTests (ACC05_AuditTimeoutCancelsWriterOperationAndPreservesDenial,
    // ACC05_Audit_writer_timeout_does_not_delay_403).
    private static readonly TimeSpan OrderingBudget = TimeSpan.FromSeconds(30);

    private string connectionString = null!;

    [OneTimeSetUp]
    public void RequireDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run access denial audit against PostgreSQL.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
    }

    [Test]
    public async Task ManyIdenticalDenials_AreEachStoredAsOwnRecord_AndNoneIsLostWhenTheHostStops()
    {
        var deniedCourse = Guid.NewGuid();
        HttpStatusCode[] statuses;

        var factory = new AuditHostFactory(connectionString, OrderingBudget);
        try
        {
            using var client = factory.CreateClient();
            statuses = await AccessDenialRequests.SendAsync(client, deniedCourse, Denials, Parallelism);
        }
        finally
        {
            // The host stops right after the last response. There is no buffer to flush.
            await factory.DisposeAsync();
        }

        var rows = await LoadDenialsAsync(deniedCourse);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(rows, Has.Count.EqualTo(Denials), "one access.denied record per 403, none merged or dropped");
            Assert.That(rows.Select(row => row.CorrelationId).Distinct().Count(), Is.EqualTo(Denials));
            Assert.That(rows.Select(row => row.ActorKind), Is.All.EqualTo(AuditActorKind.User));
            Assert.That(rows.Select(row => row.ActorId), Is.All.EqualTo(UserId));
            Assert.That(rows.Select(row => row.Outcome), Is.All.EqualTo(AuditOutcome.Denied));
            Assert.That(rows.Select(row => DetailOf(row, "action")), Is.All.EqualTo("audit.events.read"));
            Assert.That(rows.Select(row => DetailOf(row, "denialReason")), Is.All.EqualTo("policy_denied"));
        }
    }

    [Test]
    public async Task EachDenial_IsStoredBeforeItsResponseIsReturned()
    {
        var deniedCourse = Guid.NewGuid();
        await using var factory = new AuditHostFactory(connectionString, OrderingBudget);
        using var client = factory.CreateClient();

        for (var sent = 1; sent <= 10; sent++)
        {
            using var response = await AccessDenialRequests.PostAsync(client, deniedCourse);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(
                await LoadDenialsAsync(deniedCourse),
                Has.Count.EqualTo(sent),
                "the record already exists when the 403 arrives, so nothing waits in memory");
        }
    }

    private async Task<List<AuditEventRow>> LoadDenialsAsync(Guid courseId)
    {
        await using var context = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options);

        return await context.Events
            .AsNoTracking()
            .Where(row => row.EventType == AuditEventTypes.AccessDenied && row.CourseId == courseId)
            .ToListAsync();
    }

    private static string? DetailOf(AuditEventRow row, string key)
    {
        using var details = JsonDocument.Parse(row.DetailsJson);
        return details.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
    }
}

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class AccessDenialAuditFailureTests
{
    private const int Denials = 50;
    private const int Parallelism = 10;

    [Test]
    public async Task AuditStoreDown_EveryDenialIsStill403_AndRaisesExactlyOneLostAlarm()
    {
        var deniedCourse = Guid.NewGuid();
        var store = new UnreachableAuditEventStore();
        var logs = new CapturingLoggerProvider();
        await using var factory = new FailingAuditHostFactory(store, logs);
        using var client = factory.CreateClient();

        var statuses = await AccessDenialRequests.SendAsync(client, deniedCourse, Denials, Parallelism);

        var alarms = logs.Entries.Where(entry => entry.EventId.Id == AccessAuditEventIds.Lost.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Forbidden), "an audit failure never changes the denial");
            Assert.That(store.Attempts, Is.EqualTo(Denials), "every denial reaches the Audit store; none is merged or sampled");
            Assert.That(alarms, Has.Count.EqualTo(Denials), "one access.audit.lost alarm per lost record");
            Assert.That(alarms.Select(alarm => alarm.Level), Is.All.EqualTo(LogLevel.Error));
            Assert.That(alarms.Select(alarm => alarm.Message), Has.All.Contains("Status: Unavailable"));
            Assert.That(alarms.Select(alarm => alarm.Message), Has.None.Contains(deniedCourse.ToString("D")));
            Assert.That(alarms.Select(alarm => alarm.Message), Has.None.Contains(UnreachableAuditEventStore.FailureText));
        }
    }

    // Same host as AuditHostFactory, but the Audit store fails like an unreachable database.
    private sealed class FailingAuditHostFactory(IAuditEventStore store, ILoggerProvider logs)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(logs);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(AuditTestProbeController).Assembly);
                AuditTestAccess.AllowProbeCourse(services);
                services.RemoveAll<IAuditEventStore>();
                services.AddSingleton(store);
            });
        }
    }

    private sealed class UnreachableAuditEventStore : IAuditEventStore
    {
        public const string FailureText = "synthetic audit database failure";

        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public Task<AuditAppendStatus> AppendAsync(AuditEvent record, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromException<AuditAppendStatus>(new NpgsqlException(FailureText));
        }

        public Task<AuditEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromException<AuditEvent?>(new NpgsqlException(FailureText));
    }

    private sealed record CapturedLog(LogLevel Level, EventId EventId, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<CapturedLog> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new CapturedLog(logLevel, eventId, formatter(state, exception)));
        }
    }
}

internal static class AccessDenialRequests
{
    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync($"/__test/audit-events/courses/{courseId:D}", new { }, cancellationToken);

    public static async Task<HttpStatusCode[]> SendAsync(HttpClient client, Guid courseId, int count, int parallelism)
    {
        var statuses = new HttpStatusCode[count];
        await Parallel.ForAsync(
            0,
            count,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (index, cancellationToken) =>
            {
                using var response = await PostAsync(client, courseId, cancellationToken);
                statuses[index] = response.StatusCode;
            });

        return statuses;
    }
}
