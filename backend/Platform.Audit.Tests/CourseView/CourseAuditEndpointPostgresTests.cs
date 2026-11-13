using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Endpoints;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Audit.Tests.Authorization;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// S-14 end to end against PostgreSQL with the restricted runtime role: real host, real S-07 query,
/// real writer for <c>audit.events.viewed</c>. Courses are new GUIDs per run, so rows of other tests
/// in the shared database never match. Run by scripts/audit/test-postgres.ps1.
/// </summary>
[TestFixture]
[Category("AuditRuntime")]
public sealed class CourseAuditEndpointPostgresTests
{
    private static readonly Guid Teacher = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly Guid courseK = Guid.NewGuid();
    private readonly Guid courseL = Guid.NewGuid();
    private readonly List<Guid> eventsOfK = [];
    private readonly List<Guid> eventsOfL = [];

    private string connectionString = null!;
    private DbContextOptions<AuditDbContext> options = null!;

    [OneTimeSetUp]
    public async Task SeedEvents()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run the course audit endpoint against PostgreSQL.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options;

        // Same path as a producer: the real writer, validator and append-only store.
        foreach (var (course, ids) in new[] { (courseK, eventsOfK), (courseK, eventsOfK), (courseK, eventsOfK),
                     (courseL, eventsOfL), (courseL, eventsOfL) })
        {
            var id = Guid.NewGuid();
            await using var context = new AuditDbContext(options);
            var writer = new AuditWriter(new AuditDraftValidator(), new NoAmbientAuditContext(),
                new PostgresAuditEventStore(context), TimeProvider.System);
            var result = await writer.WriteAsync(
                new AuditEventDraft(AuditEventTypes.AccessDenied, AuditOutcome.Denied, course,
                    new AuditResourceRef(AuditResourceTypes.Course, course.ToString("D")),
                    new Dictionary<string, string> { ["action"] = "audit.events.read" }, id),
                new AuditContext(AuditActor.Anonymous, "s14-seed-" + id.ToString("N")));
            Assert.That(result.IsRecorded, Is.True, "Seeding needs the runtime INSERT grant.");
            ids.Add(id);
        }
    }

    private CourseAuditEndpointHostFactory Host()
    {
        var factory = new CourseAuditEndpointHostFactory { ConnectionString = connectionString };
        factory.Memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher, accountId: Teacher, courseId: courseK));
        return factory;
    }

    private static async Task<HttpResponseMessage> Get(CourseAuditEndpointHostFactory factory, Guid course)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, CourseAuditEndpointRoute.For(course) + "?type=access.denied");
        request.Headers.Add(HeaderTestSubjectProvider.HeaderName, Teacher.ToString("D"));
        return await client.SendAsync(request);
    }

    private async Task<List<AuditEventRow>> ViewRows(Guid course)
    {
        await using var read = new AuditDbContext(options);
        return await read.Events.AsNoTracking()
            .Where(row => row.CourseId == course && row.EventType == AuditEventTypes.AuditEventsViewed)
            .ToListAsync();
    }

    [Test]
    public async Task TeacherOfK_SeesOnlyKInStableOrder_AndTheViewIsStoredOnce()
    {
        using var factory = Host();

        using var response = await Get(factory, courseK);
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (Id: item.GetProperty("id").GetGuid(), At: item.GetProperty("occurredAt").GetDateTimeOffset()))
            .ToArray();
        var views = (await ViewRows(courseK)).Where(row => row.CorrelationId == correlation).ToList();
        var details = JsonSerializer.Deserialize<Dictionary<string, string>>(views.Single().DetailsJson)!;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // Exactly K's three events: none of L, and the S-07 order occurred_at DESC.
            Assert.That(items.Select(item => item.Id), Is.EquivalentTo(eventsOfK));
            Assert.That(items.Select(item => item.Id).Intersect(eventsOfL), Is.Empty);
            Assert.That(items.Select(item => item.At), Is.Ordered.Descending);
            Assert.That(views, Has.Count.EqualTo(1));
            Assert.That(views.Single().ActorId, Is.EqualTo(Teacher));
            Assert.That(views.Single().ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(details["resultCount"], Is.EqualTo("3"));
            Assert.That(details["filterType"], Is.EqualTo("access.denied"));
        });
    }

    [Test]
    public async Task TeacherOfK_Gets403ForL_AndNoViewOfLIsStored()
    {
        using var factory = Host();

        using var response = await Get(factory, courseL);
        var text = await response.Content.ReadAsStringAsync();
        var views = await ViewRows(courseL);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(eventsOfL.Any(id => text.Contains(id.ToString())), Is.False);
            Assert.That(views, Is.Empty);
        });
    }
}
