using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
public sealed class AuditPersistenceMappingTests
{
    [Test]
    public void MappingRoundTrip_PreservesEventContentTimeAndSchemaVersion()
    {
        var original = AuditEvent.Create(Guid.NewGuid(),
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero),
            StatusChange(), UserContext);

        var restored = AuditEventRow.FromDomain(original).ToDomain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.HasSameContentAs(original), Is.True);
            Assert.That(restored.OccurredAt, Is.EqualTo(original.OccurredAt));
            Assert.That(restored.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void MappingRoundTrip_PreservesAnonymousActorAndAbsentOptionalFields()
    {
        var original = AuditEvent.Create(Guid.NewGuid(), DateTimeOffset.UtcNow,
            new AuditEventDraft(AuditEventTypes.AuthLoginThrottled, AuditOutcome.Denied),
            AnonymousContext);

        var restored = AuditEventRow.FromDomain(original).ToDomain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.HasSameContentAs(original), Is.True);
            Assert.That(restored.ActorId, Is.Null);
            Assert.That(restored.ResourceId, Is.Null);
            Assert.That(restored.Details, Is.Empty);
        }
    }

    [Test]
    public void PostgresModel_GeneratesJsonbSchemaAndNonUniqueCorrelationIndex()
    {
        // Model/DDL generation only: no database connection and no claim of persistence.
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var context = new AuditDbContext(options);
        var sql = context.Database.GenerateCreateScript();
        var entity = context.Model.FindEntityType(typeof(AuditEventRow))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sql, Does.Contain("CREATE TABLE audit.audit_events"));
            Assert.That(entity.FindProperty(nameof(AuditEventRow.DetailsJson))!.GetColumnType(), Is.EqualTo("jsonb"));
            Assert.That(entity.GetIndexes().Single(index =>
                index.Properties.Count == 1 && index.Properties[0].Name == nameof(AuditEventRow.CorrelationId)).IsUnique, Is.False);
            Assert.That(entity.GetForeignKeys(), Is.Empty);
        }
    }
}
