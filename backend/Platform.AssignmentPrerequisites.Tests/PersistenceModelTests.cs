using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.AssignmentPrerequisites.Infrastructure.Persistence;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class PersistenceModelTests
{
    private AssignmentPrerequisitesDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AssignmentPrerequisitesDbContext>();
        options.UseAssignmentPrerequisitesNpgsql("Host=localhost;Database=model-only");
        _context = new AssignmentPrerequisitesDbContext(options.Options);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public void Table_IsInOwnSchema()
    {
        IEntityType entity = Entity();

        Assert.That(entity.GetSchema(), Is.EqualTo("assignment_prerequisites"));
        Assert.That(entity.GetTableName(), Is.EqualTo("prerequisites"));
    }

    [Test]
    public void PrimaryKey_IsTheRelationXRequiresY()
    {
        IKey key = Entity().FindPrimaryKey()!;

        Assert.That(
            key.Properties.Select(p => p.GetColumnName()),
            Is.EqualTo(new[] { "assignment_id", "required_assignment_id" }));
    }

    [Test]
    public void CheckConstraints_RepeatDomainRules()
    {
        IModel designModel = _context.GetService<IDesignTimeModel>().Model;
        IEntityType entity = designModel.FindEntityType(typeof(AssignmentPrerequisiteRow))!;

        string[] names = entity.GetCheckConstraints().Select(c => c.Name!).ToArray();

        Assert.That(names, Is.EquivalentTo(new[]
        {
            "ck_prerequisites_not_self",
            "ck_prerequisites_completion_requirement",
            "ck_prerequisites_course_id_format"
        }));
    }

    [Test]
    public void RequiredColumns_AreNotNullable()
    {
        IEntityType entity = Entity();

        foreach (string property in new[] { "CourseId", "CompletionRequirement", "CreatedById", "CreatedAtUtc" })
        {
            Assert.That(entity.FindProperty(property)!.IsNullable, Is.False, property);
        }
    }

    [Test]
    public void Migrations_MatchTheModel()
    {
        Assert.That(_context.Database.HasPendingModelChanges(), Is.False);
    }

    private IEntityType Entity() => _context.Model.FindEntityType(typeof(AssignmentPrerequisiteRow))!;
}
