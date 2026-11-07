using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Groups;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Groups.Data;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Groups;

/// <summary>
/// ACC-06 group memberships as the visibility check reads them, through the real
/// <see cref="GroupMembershipReader"/> and <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// Spec ACC-06: only real members of a group see its closed materials, so the reader must return
/// active memberships of active groups of the requested course and nothing else. Soft delete,
/// archiving and name ordering are Team 21 implementation behavior.
/// </para>
/// <para>
/// Exclusion tests also seed one membership that must be returned, so an empty result cannot pass
/// by accident. In memory runs the LINQ and query filters, not Postgres SQL translation.
/// </para>
/// </summary>
public class GroupMembershipReaderTests
{
    private Guid _courseId;
    private Guid _student;
    private DbContextOptions<GroupsDbContext> _dbOptions = null!;

    [SetUp]
    public void FreshDatabase()
    {
        _courseId = Guid.NewGuid();
        _student = Guid.NewGuid();

        // One in memory database per test, named by a fresh guid
        _dbOptions = new DbContextOptionsBuilder<GroupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    // RDR01. Spec ACC-06
    [Test]
    public async Task GroupsOf_ActiveMemberInActiveGroup_ReturnsMembership()
    {
        var group = await SeedGroupAsync(_courseId, "Group A");
        var member = await SeedMemberAsync(group.Id, _student);

        var result = await ReadAsync(_student, _courseId);

        var stored = await StoredMemberAsync(member.Id);
        Assert.That(result, Is.EqualTo(new[]
        {
            new GroupMembership(group.Id, "Group A", _courseId, stored.CreatedAt)
        }));
        Assert.That(result[0].JoinedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    // RDR02. Spec ACC-06; removal as a soft delete is Team 21 behavior
    [Test]
    public async Task GroupsOf_SoftDeletedMembership_IsExcluded()
    {
        var left = await SeedGroupAsync(_courseId, "Group A");
        var stillIn = await SeedGroupAsync(_courseId, "Group B");
        var removed = await SeedMemberAsync(left.Id, _student, deleted: true);
        await SeedMemberAsync(stillIn.Id, _student);

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupId), Is.EqualTo(new[] { stillIn.Id }));
        var history = await StoredMemberAsync(removed.Id);
        Assert.That(history.IsDeleted, Is.True, "the removed membership is still stored as history");
    }

    // RDR03. Spec ACC-06. The reader does not check Group.IsDeleted itself; the group's soft delete
    // filter must apply through the m.Group navigation.
    [Test]
    public async Task GroupsOf_SoftDeletedGroup_IsExcluded()
    {
        var deletedGroup = await SeedGroupAsync(_courseId, "Group A", deleted: true);
        var activeGroup = await SeedGroupAsync(_courseId, "Group B");
        await SeedMemberAsync(deletedGroup.Id, _student);
        await SeedMemberAsync(activeGroup.Id, _student);

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupId), Is.EqualTo(new[] { activeGroup.Id }));
    }

    // RDR04. Spec ACC-06; an archived group no longer grants access (Team 21 behavior)
    [Test]
    public async Task GroupsOf_ArchivedGroup_IsExcluded()
    {
        var archived = await SeedGroupAsync(_courseId, "Group A", GroupStatus.Archived);
        var active = await SeedGroupAsync(_courseId, "Group B");
        await SeedMemberAsync(archived.Id, _student);
        await SeedMemberAsync(active.Id, _student);

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupId), Is.EqualTo(new[] { active.Id }));
    }

    // RDR05. Spec ACC-06. Related: SEC-01, a group of another course never counts.
    [Test]
    public async Task GroupsOf_AnotherCourse_IsExcluded()
    {
        var otherCourse = Guid.NewGuid();
        var inCourseA = await SeedGroupAsync(_courseId, "Group A");
        var inCourseB = await SeedGroupAsync(otherCourse, "Group A");
        await SeedMemberAsync(inCourseA.Id, _student);
        await SeedMemberAsync(inCourseB.Id, _student);

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupId), Is.EqualTo(new[] { inCourseA.Id }));
        Assert.That(result.Select(m => m.CourseId), Is.All.EqualTo(_courseId));
    }

    // RDR06. Spec ACC-06: one student's memberships do not reveal another's
    [Test]
    public async Task GroupsOf_AnotherStudent_IsExcluded()
    {
        var classmate = Guid.NewGuid();
        var mine = await SeedGroupAsync(_courseId, "Group A");
        var theirs = await SeedGroupAsync(_courseId, "Group B");
        await SeedMemberAsync(mine.Id, _student);
        await SeedMemberAsync(theirs.Id, classmate);

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupId), Is.EqualTo(new[] { mine.Id }));
    }

    // RDR07. Spec ACC-06. Ordering by group name is Team 21 behavior (IGroupMembershipReader docs).
    [Test]
    public async Task GroupsOf_MultipleGroups_ReturnsGroupsSortedByName()
    {
        foreach (var name in new[] { "Gamma", "Alpha", "Delta", "Beta" })
        {
            var group = await SeedGroupAsync(_courseId, name);
            await SeedMemberAsync(group.Id, _student);
        }

        var result = await ReadAsync(_student, _courseId);

        Assert.That(result.Select(m => m.GroupName), Is.EqualTo(new[] { "Alpha", "Beta", "Delta", "Gamma" }));
    }

    // RDR08. Spec ACC-06. Rows that the empty id would match do exist, so an empty result shows the
    // empty id is refused, not just that nothing was there.
    [TestCase(true, false, TestName = "GroupsOf_EmptyAccountOrCourseId_ReturnsEmpty(empty account)")]
    [TestCase(false, true, TestName = "GroupsOf_EmptyAccountOrCourseId_ReturnsEmpty(empty course)")]
    public async Task GroupsOf_EmptyAccountOrCourseId_ReturnsEmpty(bool emptyAccount, bool emptyCourse)
    {
        var accountId = emptyAccount ? Guid.Empty : _student;
        var courseId = emptyCourse ? Guid.Empty : _courseId;
        var group = await SeedGroupAsync(courseId, "Group A");
        await SeedMemberAsync(group.Id, accountId);

        var result = await ReadAsync(accountId, courseId);

        Assert.That(result, Is.Empty);
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    // The reader runs on its own context, like a request
    private async Task<IReadOnlyList<GroupMembership>> ReadAsync(Guid accountId, Guid courseId)
    {
        await using var db = NewDb();
        return await new GroupMembershipReader(db).GroupsOfAsync(accountId, courseId);
    }

    private async Task<Group> SeedGroupAsync(
        Guid courseId, string name, GroupStatus status = GroupStatus.Active, bool deleted = false)
    {
        await using var db = NewDb();
        var group = new Group
        {
            CourseId = courseId,
            Name = name,
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

    private async Task<GroupMember> SeedMemberAsync(Guid groupId, Guid userId, bool deleted = false)
    {
        await using var db = NewDb();
        var member = new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            CreatedBy = Guid.NewGuid(),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTime.UtcNow : null
        };
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    // History read through a fresh context, bypassing only the soft delete filter
    private async Task<GroupMember> StoredMemberAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.GroupMembers
            .IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])
            .AsNoTracking()
            .SingleAsync(m => m.Id == id);
    }
}
