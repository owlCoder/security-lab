
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.Persistence;

namespace Platform.Membership.Tests.Persistence;

// ACC-02-2: proverava da je pravilo zaista u modelu baze, bez pokretanja PostgreSQL-a.
[TestFixture]
public sealed class MembershipModelTests
{
    [Test]
    public void Course_members_live_in_the_membership_schema()
    {
        using var db = CreateContext();
        var entity = DesignModel(db).FindEntityType(typeof(CourseMember))!;

        Assert.That(entity.GetSchema(), Is.EqualTo(MembershipDbContext.Schema));
        Assert.That(entity.GetTableName(), Is.EqualTo(CourseMemberConfiguration.TableName));
    }

    [Test]
    public void Only_one_active_membership_per_course_and_account()
    {
        using var db = CreateContext();
        var entity = DesignModel(db).FindEntityType(typeof(CourseMember))!;

        var index = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.That(
            index.Properties.Select(p => p.Name),
            Is.EqualTo(new[] { nameof(CourseMember.CourseId), nameof(CourseMember.AccountId) }));
        Assert.That(index.GetFilter(), Is.EqualTo(CourseMemberConfiguration.ActiveMemberFilter));
        Assert.That(index.GetDatabaseName(), Is.EqualTo(CourseMemberConfiguration.ActiveMemberIndexName));
    }

    [Test]
    public void Role_and_status_are_stored_as_readable_text()
    {
        using var db = CreateContext();
        var entity = DesignModel(db).FindEntityType(typeof(CourseMember))!;

        var role = entity.FindProperty(nameof(CourseMember.Role))!;
        var status = entity.FindProperty(nameof(CourseMember.Status))!;

        Assert.That(role.GetProviderClrType(), Is.EqualTo(typeof(string)));
        Assert.That(status.GetProviderClrType(), Is.EqualTo(typeof(string)));
    }

    private static MembershipDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MembershipDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options);

    private static IModel DesignModel(DbContext db) =>
        db.GetService<IDesignTimeModel>().Model;
}
