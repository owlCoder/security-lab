using Microsoft.Extensions.Logging;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Infrastructure.Groups.Defaults;
using Team21.Acc.Tests.Support;

namespace Team21.Acc.Tests.Groups;

public class LoggingVisibilityAuditWriterTests
{
    private static readonly DateTime OccurredAt = new(2026, 11, 7, 10, 15, 0, DateTimeKind.Utc);

    [TestCase(MaterialType.Assignment, DenialReason.NotInGroup)]
    public async Task WriteDenial_PreservesStructuredMaterialAndSuppliedAuditContext(
        MaterialType materialType, DenialReason reason)
    {
        var denial = new VisibilityDenial(
            Guid.NewGuid(), Guid.NewGuid(), new MaterialRef(materialType, Guid.NewGuid()),
            MaterialAction.Read, OccurredAt, reason, "server-material-request");

        var entry = await Write(denial);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Fields["ActorId"], Is.EqualTo(denial.ActorId));
            Assert.That(entry.Fields["CourseId"], Is.EqualTo(denial.CourseId));
            Assert.That(entry.Fields["MaterialType"], Is.EqualTo(materialType));
            Assert.That(entry.Fields["MaterialId"], Is.EqualTo(denial.Material!.MaterialId));
            Assert.That(entry.Fields["Action"], Is.EqualTo(MaterialAction.Read));
            Assert.That(entry.Fields["Reason"], Is.EqualTo(reason));
            Assert.That(entry.Fields["OccurredAt"], Is.EqualTo(OccurredAt));
            Assert.That(entry.Fields["CorrelationId"], Is.EqualTo(denial.CorrelationId));
        });
    }

    [Test]
    public async Task WriteDenial_CourseListDenialPreservesNullMaterialAndAction()
    {
        var denial = new VisibilityDenial(
            Guid.NewGuid(), Guid.NewGuid(), null, null,
            OccurredAt, DenialReason.NotCourseMember, "server-course-list-request");

        var entry = await Write(denial);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Fields["CourseId"], Is.EqualTo(denial.CourseId));
            Assert.That(entry.Fields["MaterialType"], Is.Null);
            Assert.That(entry.Fields["MaterialId"], Is.Null);
            Assert.That(entry.Fields["Action"], Is.Null);
            Assert.That(entry.Fields["Reason"], Is.EqualTo(DenialReason.NotCourseMember));
        });
    }

    private static async Task<RecordedLog> Write(VisibilityDenial denial)
    {
        var logger = new RecordingLogger<LoggingVisibilityAuditWriter>();
        IVisibilityAuditWriter writer = new LoggingVisibilityAuditWriter(logger);

        await writer.WriteDenialAsync(denial);

        Assert.That(logger.Entries, Has.Count.EqualTo(1));
        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Exception, Is.Null);
            // There is no separate Outcome field: this interface records denials only.
            Assert.That(entry.Fields.Keys, Is.EquivalentTo(new[]
            {
                "OccurredAt", "ActorId", "CourseId", "MaterialType", "MaterialId",
                "Action", "Reason", "CorrelationId", "{OriginalFormat}"
            }));
        });
        return entry;
    }
}
