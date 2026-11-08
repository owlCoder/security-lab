using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// S-14: <c>audit.events.viewed</c> through the real AuditWriter and the restricted runtime role
/// (SELECT/INSERT only). Run by scripts/audit/test-postgres.ps1.
/// </summary>
[TestFixture]
[Category("AuditRuntime")]
public sealed class CourseAuditViewPostgresTests
{
    private static readonly Guid Viewer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CourseK = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private DbContextOptions<AuditDbContext> options = null!;

    [OneTimeSetUp]
    public void Configure()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to record audit.events.viewed in PostgreSQL.");
            return;
        }

        var connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options;
    }

    [Test]
    public async Task SuccessfulView_StoresExactlyOneViewedRow_WithTheSafeSummaryOnly()
    {
        var correlation = "s14-viewed-" + Guid.NewGuid().ToString("N");
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        bool recorded;
        await using (var context = new AuditDbContext(options))
        {
            var writer = new AuditWriter(new AuditDraftValidator(), new NoAmbientAuditContext(),
                new PostgresAuditEventStore(context), TimeProvider.System);
            var recorder = new CourseAuditViewRecorder(writer, NullLogger<CourseAuditViewRecorder>.Instance);
            recorded = await recorder.RecordAsync(new CourseAuditViewSummary(
                Viewer, CourseK, correlation, AuditEventTypes.AccessDenied, AuditOutcome.Denied, from, to, 7));
        }

        await using var read = new AuditDbContext(options);
        var rows = await read.Events.AsNoTracking().Where(row => row.CorrelationId == correlation).ToListAsync();
        var row = rows.Single();
        var details = JsonSerializer.Deserialize<Dictionary<string, string>>(row.DetailsJson)!;

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.True);
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(row.EventType, Is.EqualTo(AuditEventTypes.AuditEventsViewed));
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(row.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(row.ActorId, Is.EqualTo(Viewer));
            Assert.That(row.CourseId, Is.EqualTo(CourseK));
            Assert.That(row.ResourceType, Is.EqualTo(AuditResourceTypes.Course));
            Assert.That(row.ResourceId, Is.EqualTo(CourseK.ToString("D")));
            Assert.That(row.OccurredAt, Is.InRange(before, DateTimeOffset.UtcNow.AddSeconds(1)));
            Assert.That(details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["filterType"] = "access.denied",
                ["filterOutcome"] = "denied",
                ["filterFrom"] = "2026-11-01T00:00:00.0000000Z",
                ["filterTo"] = "2026-11-08T00:00:00.0000000Z",
                ["resultCount"] = "7"
            }));
        });
    }
}
