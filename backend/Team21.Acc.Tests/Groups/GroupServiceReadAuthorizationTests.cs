using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Dtos;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Groups.Data;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Groups;

/// <summary>
/// ACC-06 read operations of <see cref="GroupService"/> (GetGroupsAsync, GetGroupAsync,
/// GetMembersAsync): who may read groups and their members, through the real service and the real
/// <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// Spec ACC-06: groups and restricted visibility per group. Spec 2.1 / SEC-01: authorization on the
/// server, a denial does not reveal the resource (IDOR). Team 21 implementation: only an active teacher
/// of the course manages its groups; every denial of a signed-in caller is NotFound, worded like a
/// missing course or group.
/// </para>
/// <para>
/// The world holds two courses, each with a teacher and a group of two real members, so every
/// denial is checked against data that exists and that the course's own teacher can read.
/// Account status, course membership and course state are doubles of the other modules' readers.
/// </para>
/// </summary>
public class GroupServiceReadAuthorizationTests
{
    private Guid _courseA;
    private Guid _courseB;
    private Guid _teacherA;
    private Guid _teacherB;
    private Guid _studentA;
    private Group _groupA = null!;
    private Group _groupB = null!;
    private List<GroupMember> _membersA = null!;
    private List<GroupMember> _membersB = null!;

    private DbContextOptions<GroupsDbContext> _dbOptions = null!;
    private AccountStatuses _accounts = null!;
    private CourseMemberships _courseMembers = null!;
    private CourseStates _courseStates = null!;

    [SetUp]
    public async Task TwoCoursesWithMembers()
    {
        _courseA = Guid.NewGuid();
        _courseB = Guid.NewGuid();
        _teacherA = Guid.NewGuid();
        _teacherB = Guid.NewGuid();
        _studentA = Guid.NewGuid();

        // One in memory database per test, named by a fresh guid
        _dbOptions = new DbContextOptionsBuilder<GroupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _accounts = new AccountStatuses();
        _courseMembers = new CourseMemberships();
        _courseStates = new CourseStates();

        _courseStates.Set(_courseA, CourseState.Active);
        _courseStates.Set(_courseB, CourseState.Active);
        GivenMember(_teacherA, _courseA, CourseRole.Teacher);
        GivenMember(_teacherB, _courseB, CourseRole.Teacher);
        GivenMember(_studentA, _courseA, CourseRole.Student);

        _groupA = await SeedGroupAsync(_courseA, "Group A");
        _groupB = await SeedGroupAsync(_courseB, "Group B");
        _membersA = [await SeedMemberAsync(_groupA, _studentA), await SeedMemberAsync(_groupA, Guid.NewGuid())];
        _membersB = [await SeedMemberAsync(_groupB, Guid.NewGuid()), await SeedMemberAsync(_groupB, Guid.NewGuid())];
    }

    // R01. Primary: ACC-06 (positive baseline). Related: SEC-01, the list is scoped to the requested
    // course. The teacher of course A reads the persisted group and both members; course B's group,
    // which exists in the same database, is not listed.
    [Test]
    public async Task Read_TeacherOfCourse_ReturnsPersistedGroupsAndMembers()
    {
        await using var db = NewDb();
        var service = CreateService(_teacherA, db);

        var groups = await service.GetGroupsAsync(_courseA);
        var group = await service.GetGroupAsync(_courseA, _groupA.Id);
        var members = await service.GetMembersAsync(_courseA, _groupA.Id);

        var expectedGroup = new GroupDto(_groupA.Id, _courseA, "Group A", nameof(GroupStatus.Active));
        Assert.That(groups.IsSuccess, Is.True);
        Assert.That(groups.Value, Is.EqualTo(new[] { expectedGroup }));
        Assert.That(group.IsSuccess, Is.True);
        Assert.That(group.Value, Is.EqualTo(expectedGroup));
        Assert.That(members.IsSuccess, Is.True);
        Assert.That(members.Value, Is.EquivalentTo(_membersA.Select(ToDto)));
    }

    // R02. Primary: ACC-06. Related: SEC-01 / 2.1, no caller, no data. 401 is the Team 21 mapping of
    // Unauthorized.
    [Test]
    public async Task GetGroups_Anonymous_ReturnsUnauthorized()
    {
        await using var db = NewDb();
        var service = CreateService(userId: null, db);

        var result = await service.GetGroupsAsync(_courseA);

        AssertFailedWith(result, ErrorType.Unauthorized);
        Assert.That(result.Value, Is.Null);
        AssertMessageNamesNothing(result.Error!, _courseA, _groupA.Id);
    }

    // R03. Primary: ACC-06 (group management belongs to the teacher). Related: SEC-01, a student is
    // answered exactly like for a course that does not exist, so the group list stays hidden.
    [Test]
    public async Task GetGroups_EnrolledStudent_ReturnsNotFound()
    {
        Assert.That(await TeacherSeesGroupAsync(_teacherA, _courseA, _groupA), Is.True,
            "precondition: the course has a group its teacher can list");
        await using var db = NewDb();
        var service = CreateService(_studentA, db);

        var result = await service.GetGroupsAsync(_courseA);
        var unknownCourse = await service.GetGroupsAsync(Guid.NewGuid());

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error, Is.EqualTo(unknownCourse.Error));
        AssertMessageNamesNothing(result.Error!, _courseA, _groupA.Id);
    }

    // R04. Primary: ACC-06. Related: SEC-01, the member list holds other students' identities. The
    // caller is an enrolled student who is even a member of this group, and still gets nothing.
    [Test]
    public async Task GetMembers_EnrolledStudent_ReturnsNotFoundWithoutMemberIdentities()
    {
        Assert.That(await TeacherSeesMembersAsync(_teacherA, _courseA, _groupA, _membersA), Is.True,
            "precondition: the group has members its teacher can list");
        await using var db = NewDb();
        var service = CreateService(_studentA, db);

        var result = await service.GetMembersAsync(_courseA, _groupA.Id);
        var unknownCourse = await service.GetMembersAsync(Guid.NewGuid(), _groupA.Id);

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error, Is.EqualTo(unknownCourse.Error));
        AssertMessageNamesNothing(result.Error!, [.. _membersA.Select(m => m.UserId), .. _membersA.Select(m => m.Id)]);
    }

    // R05. Primary: ACC-06. Related: SEC-01 (IDOR). The teacher of course A asks for course B's group,
    // through course A (the group id is checked against the course) and through course B directly (the
    // teacher is not its teacher). Each answer equals the answer for an id that does not exist.
    [Test]
    public async Task GetGroup_TeacherOfOtherCourse_ReturnsNotFound()
    {
        Assert.That(await TeacherSeesGroupAsync(_teacherB, _courseB, _groupB), Is.True,
            "precondition: course B's teacher can read its group");
        await using var db = NewDb();
        var service = CreateService(_teacherA, db);

        var throughOwnCourse = await service.GetGroupAsync(_courseA, _groupB.Id);
        var unknownGroup = await service.GetGroupAsync(_courseA, Guid.NewGuid());
        var throughForeignCourse = await service.GetGroupAsync(_courseB, _groupB.Id);
        var unknownCourse = await service.GetGroupAsync(Guid.NewGuid(), _groupB.Id);

        AssertFailedWith(throughOwnCourse, ErrorType.NotFound);
        Assert.That(throughOwnCourse.Value, Is.Null);
        Assert.That(throughOwnCourse.Error, Is.EqualTo(unknownGroup.Error));
        AssertFailedWith(throughForeignCourse, ErrorType.NotFound);
        Assert.That(throughForeignCourse.Value, Is.Null);
        Assert.That(throughForeignCourse.Error, Is.EqualTo(unknownCourse.Error));
        AssertMessageNamesNothing(throughOwnCourse.Error!, _courseB, _groupB.Id);
        AssertMessageNamesNothing(throughForeignCourse.Error!, _courseB, _groupB.Id);
    }

    // R06. Primary: ACC-06. Related: SEC-01 (IDOR), read is side-effect free. Same two routes as R05
    // for the member list; course B's members stay hidden and untouched.
    [Test]
    public async Task GetMembers_TeacherOfOtherCourse_ReturnsNotFoundAndLeavesDataUnchanged()
    {
        Assert.That(await TeacherSeesMembersAsync(_teacherB, _courseB, _groupB, _membersB), Is.True,
            "precondition: course B's teacher can list its members");
        await using var db = NewDb();
        var service = CreateService(_teacherA, db);

        var throughOwnCourse = await service.GetMembersAsync(_courseA, _groupB.Id);
        var unknownGroup = await service.GetMembersAsync(_courseA, Guid.NewGuid());
        var throughForeignCourse = await service.GetMembersAsync(_courseB, _groupB.Id);
        var unknownCourse = await service.GetMembersAsync(Guid.NewGuid(), _groupB.Id);

        AssertFailedWith(throughOwnCourse, ErrorType.NotFound);
        Assert.That(throughOwnCourse.Value, Is.Null);
        Assert.That(throughOwnCourse.Error, Is.EqualTo(unknownGroup.Error));
        AssertFailedWith(throughForeignCourse, ErrorType.NotFound);
        Assert.That(throughForeignCourse.Value, Is.Null);
        Assert.That(throughForeignCourse.Error, Is.EqualTo(unknownCourse.Error));
        Guid[] hidden = [.. _membersB.Select(m => m.UserId), .. _membersB.Select(m => m.Id)];
        AssertMessageNamesNothing(throughOwnCourse.Error!, hidden);
        AssertMessageNamesNothing(throughForeignCourse.Error!, hidden);
        Assert.That(await StoredMembersOfAsync(_groupB.Id), Is.EquivalentTo(_membersB.Select(Snapshot)),
            "course B's members are stored exactly as seeded");
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    private GroupService CreateService(Guid? userId, GroupsDbContext db) =>
        new(db, new StubCurrentActor(userId), _courseMembers, _accounts, _courseStates, new DiscardingAuditTrail());

    private void GivenMember(Guid userId, Guid courseId, CourseRole role)
    {
        _accounts.Set(userId, AccountStatus.Active);
        _courseMembers.Set(userId, new CourseMembership(courseId, role, Active: true, CanReview: role == CourseRole.Teacher));
    }

    // Seeding and reading use their own contexts, so the service starts with nothing tracked and
    // persisted state is checked independently of what the service returned.
    private async Task<Group> SeedGroupAsync(Guid courseId, string name)
    {
        await using var db = NewDb();
        var group = new Group { CourseId = courseId, Name = name, CreatedBy = Guid.NewGuid() };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private async Task<GroupMember> SeedMemberAsync(Group group, Guid userId)
    {
        await using var db = NewDb();
        var member = new GroupMember { GroupId = group.Id, UserId = userId, CreatedBy = group.CreatedBy };
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    // Positive controls: the data a denial hides exists and is readable by the right teacher
    private async Task<bool> TeacherSeesGroupAsync(Guid teacher, Guid courseId, Group group)
    {
        await using var db = NewDb();
        var groups = await CreateService(teacher, db).GetGroupsAsync(courseId);
        return groups.IsSuccess && groups.Value!.Any(g => g.Id == group.Id);
    }

    private async Task<bool> TeacherSeesMembersAsync(
        Guid teacher, Guid courseId, Group group, List<GroupMember> expected)
    {
        await using var db = NewDb();
        var members = await CreateService(teacher, db).GetMembersAsync(courseId, group.Id);
        return members.IsSuccess
            && members.Value!.Select(m => m.Id).ToHashSet().SetEquals(expected.Select(m => m.Id));
    }

    private async Task<List<MemberSnapshot>> StoredMembersOfAsync(Guid groupId)
    {
        await using var db = NewDb();
        var stored = await db.GroupMembers
            .IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();
        return stored.Select(Snapshot).ToList();
    }

    private static GroupMemberDto ToDto(GroupMember m) => new(m.Id, m.GroupId, m.UserId, m.CreatedAt);

    private static MemberSnapshot Snapshot(GroupMember m) =>
        new(m.Id, m.GroupId, m.UserId, m.CreatedAt, m.CreatedBy,
            m.IsDeleted, m.UpdatedAt, m.UpdatedBy, m.DeletedAt, m.DeletedBy);

    private static void AssertFailedWith<T>(Result<T> result, ErrorType type)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(type));
    }

    // The message goes out as is, so it must not carry any id of the protected data
    private static void AssertMessageNamesNothing(Error error, params Guid[] ids)
    {
        foreach (var id in ids)
            Assert.That(error.Message, Does.Not.Contain(id.ToString()));
    }

    // Everything a read could change on a stored member row
    private sealed record MemberSnapshot(
        Guid Id, Guid GroupId, Guid UserId, DateTime CreatedAt, Guid CreatedBy,
        bool IsDeleted, DateTime? UpdatedAt, Guid? UpdatedBy, DateTime? DeletedAt, Guid? DeletedBy);

    // Test doubles: one small class per interface GroupService depends on

    // Reads write no audit records; the audit trail is not what these tests check
    private sealed class DiscardingAuditTrail : IAuditTrail
    {
        public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubCurrentActor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId { get; } = userId;
        public bool IsAuthenticated => UserId is not null;
    }

    // Unknown accounts are inactive, like the deny-all default
    private sealed class AccountStatuses : IAccountStatusReader
    {
        private readonly Dictionary<Guid, AccountStatus> _statuses = new();

        public void Set(Guid accountId, AccountStatus status) => _statuses[accountId] = status;

        public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(_statuses.GetValueOrDefault(accountId, AccountStatus.Inactive));
    }

    // Honest double: a membership is only ever returned for its own course
    private sealed class CourseMemberships : ICourseMembershipReader
    {
        private readonly Dictionary<(Guid, Guid), CourseMembership> _memberships = new();

        public void Set(Guid accountId, CourseMembership membership) =>
            _memberships[(accountId, membership.CourseId)] = membership;

        public Task<CourseMembership?> FindAsync(Guid accountId, Guid courseId, CancellationToken ct = default) =>
            Task.FromResult(_memberships.GetValueOrDefault((accountId, courseId)));
    }

    private sealed class CourseStates : ICourseStateReader
    {
        private readonly Dictionary<Guid, CourseState> _states = new();

        public void Set(Guid courseId, CourseState state) => _states[courseId] = state;

        public Task<CourseState?> GetStateAsync(Guid courseId, CancellationToken ct = default) =>
            Task.FromResult(_states.TryGetValue(courseId, out var state) ? state : (CourseState?)null);
    }
}
