using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

using LabVisibilityCourseRole = Platform.LabVisibility.Contracts.CourseRole;

namespace Platform.Audit.Tests;

// AUT-04 / AUD-01 / AUD-03 (#226): stvarni tok LabVisibility → AuditWriterLabVisibilityAudit →
// registrovani ITrustedAuditWriter → PostgreSQL, kroz pravi Platform.Api host. Zapis se čita
// iz novog AuditDbContext-a: akter, radnja, kurs i correlation ID su tačni, a sadržaja ključa nema.
[TestFixture]
[Category("AuditRuntime")]
[Property("RequirementId", "AUT-04")]
public sealed class LabVisibilityAuditPostgresTests
{
    private const string SecretAnswer = "SECRET-ANSWER-POSTGRES-7f3a";

    private string connectionString = null!;

    [OneTimeSetUp]
    public void RequireDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run LabVisibility audit against PostgreSQL.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
    }

    [Test]
    public async Task KeyReads_AreStoredWithActorActionAndCorrelation_WithoutKeyContent()
    {
        var courseId = Guid.NewGuid();
        var labId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var teacherCorrelation = "lv-teacher-" + Guid.NewGuid().ToString("N");
        var studentCorrelation = "lv-student-" + Guid.NewGuid().ToString("N");
        var evaluatorCorrelation = "lv-evaluator-" + Guid.NewGuid().ToString("N");

        EvaluatorKey? teacherKey;
        EvaluatorKey? studentKey;
        EvaluatorKey? evaluatorKey;

        await using (var factory = new AuditHostFactory(connectionString))
        {
            var services = factory.Services;
            Assert.That(
                services.GetRequiredService<ILabVisibilityAudit>(),
                Is.InstanceOf<AuditWriterLabVisibilityAudit>(),
                "the host must use the durable audit adapter, not the log fallback");

            var content = LabPublicContent.Create(
                labId,
                courseId,
                teacherId,
                LabVisibilityState.Published,
                "Analiza dnevnika",
                "Pronađi povezane događaje",
                [new PublicTask("t1", 1, "Koji događaj je prvi?", ["A", "B"])]);
            services.GetRequiredService<InMemoryLabPublicContentReader>().Save(content);
            await services.GetRequiredService<IEvaluatorKeyStore>().SaveAsync(
                EvaluatorKey.Create(content, [new TaskAnswerKey("t1", SecretAnswer)]));

            var membership = services.GetRequiredService<InMemoryCourseMembership>();
            membership.Set(courseId, teacherId, LabVisibilityCourseRole.Teacher);
            membership.Set(courseId, studentId, LabVisibilityCourseRole.Student);

            var reader = services.GetRequiredService<EvaluatorKeyReader>();
            teacherKey = await reader.ReadAsync(
                new SubjectRef(teacherId.ToString(), AccountState.Active), labId, teacherCorrelation);
            studentKey = await reader.ReadAsync(
                new SubjectRef(studentId.ToString(), AccountState.Active), labId, studentCorrelation);
            evaluatorKey = await reader.ReadAsync(
                new SubjectRef(EvaluatorPrincipal.Id, AccountState.Active), labId, evaluatorCorrelation);
        }

        var rows = await LoadAsync(labId);
        var teacherRow = rows.Single(row => row.CorrelationId == teacherCorrelation);
        var studentRow = rows.Single(row => row.CorrelationId == studentCorrelation);
        var evaluatorRow = rows.Single(row => row.CorrelationId == evaluatorCorrelation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(teacherKey, Is.Not.Null);
            Assert.That(studentKey, Is.Null);
            Assert.That(evaluatorKey, Is.Not.Null);
            Assert.That(rows, Has.Count.EqualTo(3), "one record per read, nothing merged or dropped");

            Assert.That(teacherRow.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(teacherRow.ActorId, Is.EqualTo(teacherId));
            Assert.That(teacherRow.Outcome, Is.EqualTo(AuditOutcome.Success));

            Assert.That(studentRow.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(studentRow.ActorId, Is.EqualTo(studentId));
            Assert.That(studentRow.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(DetailOf(studentRow, "reasonCode"), Is.EqualTo("forbidden"));

            Assert.That(evaluatorRow.ActorKind, Is.EqualTo(AuditActorKind.Service));
            Assert.That(evaluatorRow.ActorId, Is.EqualTo(EvaluatorPrincipal.ServiceActorId));
            Assert.That(evaluatorRow.Outcome, Is.EqualTo(AuditOutcome.Success));

            foreach (var row in rows)
            {
                Assert.That(row.CourseId, Is.EqualTo(courseId), "course comes from LabPublicContent, not from the request");
                Assert.That(row.EventType, Is.EqualTo(AuditEventTypes.LabVisibilityAccessed));
                Assert.That(DetailOf(row, "action"), Is.EqualTo(LabVisibilityActions.KeyRead));
                Assert.That(DetailOf(row, "laboratoryId"), Is.EqualTo(labId.ToString()));
                Assert.That(row.DetailsJson, Does.Not.Contain(SecretAnswer));
                Assert.That(JsonSerializer.Serialize(row), Does.Not.Contain(SecretAnswer));
            }
        }
    }

    // Negativni test nad bazom: nepoznata radnja iz istog adaptera ne stiže u PostgreSQL.
    [Test]
    public async Task UnknownAction_NeverReachesTheDatabase()
    {
        var labId = Guid.NewGuid();

        await using (var factory = new AuditHostFactory(connectionString))
        {
            var audit = factory.Services.GetRequiredService<ILabVisibilityAudit>();
            await audit.WriteAsync(new LabVisibilityAuditEvent(
                "lab.answers.dump",
                labId,
                Guid.NewGuid().ToString(),
                "Allow",
                "lv-unknown-" + Guid.NewGuid().ToString("N")));
        }

        Assert.That(await LoadAsync(labId), Is.Empty);
    }

    private async Task<List<AuditEventRow>> LoadAsync(Guid labId)
    {
        await using var context = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options);

        var rows = await context.Events
            .AsNoTracking()
            .Where(row => row.EventType == AuditEventTypes.LabVisibilityAccessed)
            .ToListAsync();

        return rows.Where(row => DetailOf(row, "laboratoryId") == labId.ToString()).ToList();
    }

    private static string? DetailOf(AuditEventRow row, string key)
    {
        using var details = JsonDocument.Parse(row.DetailsJson);
        return details.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
    }
}
