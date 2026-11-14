using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Groups;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Groups.Data;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Groups;

/// <summary>
/// ACC-06 material visibility rules as the visibility check reads them, through the real
/// <see cref="MaterialAccessRuleReader"/> and <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// Spec ACC-06: members of one group do not see the closed materials of another, so a rule may only
/// name groups that really hold access. Team 21 implementation: a material is CourseWide or
/// RestrictedToGroups, grants are GroupMaterialAccess rows, and only active, non revoked grants for
/// active, non deleted groups of the material's own course count.
/// </para>
/// <para>
/// Exclusion tests also seed a grant that must be returned, so an empty list cannot pass by accident.
/// In memory runs the LINQ and query filters, not Postgres SQL, foreign keys or unique indexes.
/// </para>
/// </summary>
public class MaterialAccessRuleReaderTests
{
    private Guid _courseId;
    private DbContextOptions<GroupsDbContext> _dbOptions = null!;

    [SetUp]
    public void FreshDatabase()
    {
        _courseId = Guid.NewGuid();

        // One in memory database per test, named by a fresh guid
        _dbOptions = new DbContextOptionsBuilder<GroupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    // MAR01. Spec ACC-06
    [Test]
    public async Task Find_RestrictedMaterial_ReturnsActiveAllowedGroups()
    {
        var groupA = await SeedGroupAsync(_courseId);
        var groupB = await SeedGroupAsync(_courseId);
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(audience.Id, groupA.Id);
        await SeedGrantAsync(audience.Id, groupB.Id);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(rule!.Material, Is.EqualTo(RefOf(audience)));
            Assert.That(rule.CourseId, Is.EqualTo(_courseId));
            Assert.That(rule.Visibility, Is.EqualTo(MaterialVisibility.RestrictedToGroups));
            Assert.That(rule.AllowedGroupIds, Is.EquivalentTo(new[] { groupA.Id, groupB.Id }));
        });
    }

    // MAR02. Spec ACC-06; CourseWide is the Team 21 name for "open to the whole course"
    [Test]
    public async Task Find_CourseWideMaterial_ReturnsCourseWideRule()
    {
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule, Is.Not.Null, "a course-wide rule exists without any group grant");
        Assert.Multiple(() =>
        {
            Assert.That(rule!.Material, Is.EqualTo(RefOf(audience)));
            Assert.That(rule.CourseId, Is.EqualTo(_courseId));
            Assert.That(rule.Visibility, Is.EqualTo(MaterialVisibility.CourseWide));
            Assert.That(rule.AllowedGroupIds, Is.Empty);
        });
    }

    // MAR03. Spec ACC-06 with ACC-05 default deny: a closed material nobody was granted is still a rule
    // (and so still closed), not a missing rule and not a course-wide one.
    [Test]
    public async Task Find_RestrictedMaterialWithNoGrants_ReturnsEmptyAllowedGroups()
    {
        await SeedGroupAsync(_courseId);
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule, Is.Not.Null);
        Assert.That(rule!.Visibility, Is.EqualTo(MaterialVisibility.RestrictedToGroups));
        Assert.That(rule.AllowedGroupIds, Is.Empty);
    }

    // MAR04. Spec ACC-06; revoking as a soft delete is Team 21 behavior
    [Test]
    public async Task Find_RevokedGroupPermission_IsExcluded()
    {
        var kept = await SeedGroupAsync(_courseId);
        var revokedGroup = await SeedGroupAsync(_courseId);
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(audience.Id, kept.Id);
        var revoked = await SeedGrantAsync(audience.Id, revokedGroup.Id, deleted: true);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule!.AllowedGroupIds, Is.EqualTo(new[] { kept.Id }));
        var history = await StoredGrantAsync(revoked.Id);
        Assert.That(history.IsDeleted, Is.True, "the revoked grant is still stored as history");
    }

    // MAR05. Spec ACC-06; an archived group no longer grants access (Team 21 behavior)
    [Test]
    public async Task Find_ArchivedGroupPermission_IsExcluded()
    {
        var active = await SeedGroupAsync(_courseId);
        var archived = await SeedGroupAsync(_courseId, GroupStatus.Archived);
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(audience.Id, active.Id);
        await SeedGrantAsync(audience.Id, archived.Id);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule!.AllowedGroupIds, Is.EqualTo(new[] { active.Id }));
    }

    // MAR06. Spec ACC-06. The reader does not check Group.IsDeleted itself; the group's soft delete
    // filter must apply through the g.Group navigation inside the projection.
    [Test]
    public async Task Find_SoftDeletedGroupPermission_IsExcluded()
    {
        var active = await SeedGroupAsync(_courseId);
        var deletedGroup = await SeedGroupAsync(_courseId, deleted: true);
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(audience.Id, active.Id);
        await SeedGrantAsync(audience.Id, deletedGroup.Id);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule!.AllowedGroupIds, Is.EqualTo(new[] { active.Id }));
    }

    // MAR07. Spec ACC-06. Related: SEC-01. Defense in depth: the service never writes such a grant and
    // there is no database rule against it, so the reader must drop a grant to another course's group.
    [Test]
    public async Task Find_GroupFromAnotherCourse_IsExcluded()
    {
        var ownGroup = await SeedGroupAsync(_courseId);
        var foreignGroup = await SeedGroupAsync(Guid.NewGuid());
        var audience = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(audience.Id, ownGroup.Id);
        await SeedGrantAsync(audience.Id, foreignGroup.Id);

        var rule = await FindAsync(RefOf(audience));

        Assert.That(rule!.AllowedGroupIds, Is.EqualTo(new[] { ownGroup.Id }));
    }

    // MAR08. Spec ACC-06 with ACC-05 default deny: no rule means no access. Each case stores a row the
    // lookup could wrongly match, so null shows the reference is refused, not just that nothing exists.
    // A material is identified by (type, id), never by the id alone.
    [TestCase("unknown material id")]
    [TestCase("soft-deleted audience")]
    [TestCase("empty material id")]
    [TestCase("undefined material type")]
    [TestCase("same id, other type")]
    public async Task Find_UnknownRemovedOrInvalidReference_ReturnsNull(string scenario)
    {
        var lookup = await SeedNearMissAsync(scenario);

        var rule = await FindAsync(lookup);

        Assert.That(rule, Is.Null);
    }

    // MAR09. Spec ACC-06
    [Test]
    public async Task ListByCourse_ReturnsOnlyActiveRulesOfRequestedCourse()
    {
        var group = await SeedGroupAsync(_courseId);
        var courseWide = await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide);
        var restricted = await SeedAudienceAsync(_courseId, MaterialVisibility.RestrictedToGroups);
        await SeedGrantAsync(restricted.Id, group.Id);
        var otherCourse = await SeedAudienceAsync(Guid.NewGuid(), MaterialVisibility.CourseWide);
        var deleted = await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide, deleted: true);

        var rules = await ListAsync(_courseId);

        Assert.That(rules.Select(r => r.Material), Is.EquivalentTo(new[] { RefOf(courseWide), RefOf(restricted) }));
        Assert.That(rules.Select(r => r.Material), Does.Not.Contain(RefOf(otherCourse)).And.Not.Contain(RefOf(deleted)));
        var wide = rules.Single(r => r.Material == RefOf(courseWide));
        var closed = rules.Single(r => r.Material == RefOf(restricted));
        Assert.Multiple(() =>
        {
            Assert.That(rules.Select(r => r.CourseId), Is.All.EqualTo(_courseId));
            Assert.That(wide.Visibility, Is.EqualTo(MaterialVisibility.CourseWide));
            Assert.That(wide.AllowedGroupIds, Is.Empty);
            Assert.That(closed.Visibility, Is.EqualTo(MaterialVisibility.RestrictedToGroups));
            Assert.That(closed.AllowedGroupIds, Is.EqualTo(new[] { group.Id }));
        });
    }

    // MAR09. Spec ACC-06. A rule stored under the empty course id exists, so the empty list shows the
    // empty id is refused.
    [Test]
    public async Task ListByCourse_EmptyCourseId_ReturnsEmpty()
    {
        await SeedAudienceAsync(Guid.Empty, MaterialVisibility.CourseWide);

        var rules = await ListAsync(Guid.Empty);

        Assert.That(rules, Is.Empty);
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    private static MaterialRef RefOf(MaterialAudience audience) => new(audience.MaterialType, audience.MaterialId);

    // The reader runs on its own context, like a request
    private async Task<MaterialAccessRule?> FindAsync(MaterialRef material)
    {
        await using var db = NewDb();
        return await new MaterialAccessRuleReader(db).FindAsync(material);
    }

    private async Task<IReadOnlyList<MaterialAccessRule>> ListAsync(Guid courseId)
    {
        await using var db = NewDb();
        return await new MaterialAccessRuleReader(db).ListByCourseAsync(courseId);
    }

    // Stores one row for the scenario and returns the reference to look up
    private async Task<MaterialRef> SeedNearMissAsync(string scenario)
    {
        switch (scenario)
        {
            case "unknown material id":
                await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide);
                return new MaterialRef(MaterialType.Assignment, Guid.NewGuid());

            case "soft-deleted audience":
                return RefOf(await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide, deleted: true));

            case "empty material id":
                return RefOf(await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide, materialId: Guid.Empty));

            case "undefined material type":
                return RefOf(await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide, type: (MaterialType)99));

            case "same id, other type":
                var assignment = await SeedAudienceAsync(_courseId, MaterialVisibility.CourseWide, type: MaterialType.Assignment);
                return new MaterialRef(MaterialType.Attachment, assignment.MaterialId);

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private async Task<Group> SeedGroupAsync(Guid courseId, GroupStatus status = GroupStatus.Active, bool deleted = false)
    {
        await using var db = NewDb();
        var group = new Group
        {
            CourseId = courseId,
            Name = "Group " + Guid.NewGuid().ToString("N")[..8],
            Status = status,
            ArchivedAt = status == GroupStatus.Archived ? DateTime.UtcNow : null,
            CreatedBy = Guid.NewGuid(),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private async Task<MaterialAudience> SeedAudienceAsync(
        Guid courseId,
        MaterialVisibility visibility,
        MaterialType type = MaterialType.Assignment,
        Guid? materialId = null,
        bool deleted = false)
    {
        await using var db = NewDb();
        var audience = new MaterialAudience
        {
            CourseId = courseId,
            MaterialType = type,
            MaterialId = materialId ?? Guid.NewGuid(),
            Visibility = visibility,
            CreatedBy = Guid.NewGuid(),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null
        };
        db.MaterialAudiences.Add(audience);
        await db.SaveChangesAsync();
        return audience;
    }

    private async Task<GroupMaterialAccess> SeedGrantAsync(Guid audienceId, Guid groupId, bool deleted = false)
    {
        await using var db = NewDb();
        var grant = new GroupMaterialAccess
        {
            MaterialAudienceId = audienceId,
            GroupId = groupId,
            CreatedBy = Guid.NewGuid(),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null
        };
        db.GroupMaterialAccesses.Add(grant);
        await db.SaveChangesAsync();
        return grant;
    }

    // History read through a fresh context, bypassing only the soft delete filter
    private async Task<GroupMaterialAccess> StoredGrantAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.GroupMaterialAccesses
            .IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])
            .AsNoTracking()
            .SingleAsync(a => a.Id == id);
    }
}
