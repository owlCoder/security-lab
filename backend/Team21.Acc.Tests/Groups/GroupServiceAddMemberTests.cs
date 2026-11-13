using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Dtos;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Groups.Data;
using Team21.Acc.Infrastructure.Groups.Services;

namespace Team21.Acc.Tests.Groups;

/// <summary>
/// ACC-06 adding a student to a group, through the real <see cref="GroupService"/> and the real
/// <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// Every test starts from a world where the add would succeed (M01) and breaks exactly one condition,
/// so a denial can only come from that condition. Enrollment (ACC-02/03), account status (IDN-03) and
/// course state (ACC-01) are small doubles of the other modules' readers, not their real adapters.
/// In memory enforces no unique index, so duplicate checks here prove the service, not Postgres.
/// </para>
/// </summary>
public class GroupServiceAddMemberTests
{
    private static readonly DateTime ArchivedAt = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private Guid _courseId;
    private Guid _teacher;
    private Guid _student;
    private Group _group = null!;

    private DbContextOptions<GroupsDbContext> _dbOptions = null!;
    private AccountStatuses _accounts = null!;
    private CourseMemberships _courseMembers = null!;
    private CourseStates _courseStates = null!;
    private RecordingAuditTrail _audit = null!;

    [SetUp]
    public async Task ValidWorld()
    {
        _courseId = Guid.NewGuid();
        _teacher = Guid.NewGuid();
        _student = Guid.NewGuid();

        // One in memory database per test, named by a fresh guid
        _dbOptions = new DbContextOptionsBuilder<GroupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _accounts = new AccountStatuses();
        _courseMembers = new CourseMemberships();
        _courseStates = new CourseStates();
        _audit = new RecordingAuditTrail();

        _courseStates.Set(_courseId, CourseState.Active);
        GivenMember(_teacher, _courseId, CourseRole.Teacher);
        GivenMember(_student, _courseId, CourseRole.Student);
        _group = await SeedGroupAsync(_courseId, "Group A", GroupStatus.Active);
    }

    // M01. Spec ACC-06 (positive). Related: 2.1 / AUD-01, privileged action audited.
    [Test]
    public async Task AddMember_ActiveEnrolledStudent_PersistsMembership()
    {
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(dto.GroupId, Is.EqualTo(_group.Id));
            Assert.That(dto.UserId, Is.EqualTo(_student));
            Assert.That(dto.JoinedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });

        var stored = await StoredMembersAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(dto.Id));
            Assert.That(stored[0].GroupId, Is.EqualTo(_group.Id));
            Assert.That(stored[0].UserId, Is.EqualTo(_student));
            Assert.That(stored[0].CreatedBy, Is.EqualTo(_teacher));
            Assert.That(stored[0].IsDeleted, Is.False);
        });

        // Team 21: the audit resource is the group id (the interface takes a single id)
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.add", _group.Id, AuditOutcome.Success)
        }));
    }

    // M02. Spec ACC-06; ACC-02 "no double enrollment of the same active member" applied to groups is
    // Team 21 behavior. Service check only, not the Postgres unique index.
    [Test]
    public async Task AddMember_AlreadyActiveMember_ReturnsConflict()
    {
        var existing = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Conflict);
        var stored = await StoredMembersAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(existing.Id));
            Assert.That(stored[0].CreatedBy, Is.EqualTo(existing.CreatedBy));
            Assert.That(stored[0].CreatedAt, Is.EqualTo(existing.CreatedAt));
            Assert.That(stored[0].UpdatedAt, Is.Null);
            Assert.That(stored[0].IsDeleted, Is.False);
        });
    }

    // M03. Spec ACC-06: groups are part of a course. Enrollment itself is ACC-02, read through a double.
    [Test]
    public async Task AddMember_StudentNotEnrolled_ReturnsValidation()
    {
        _courseMembers.Remove(_student, _courseId);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Validation);
        Assert.That(await StoredMembersAsync(), Is.Empty);
    }

    // M04. Spec ACC-06; an inactive course membership does not count (Team 21 behavior)
    [Test]
    public async Task AddMember_InactiveCourseMembership_ReturnsValidation()
    {
        _courseMembers.Set(_student, new CourseMembership(_courseId, CourseRole.Student, Active: false, CanReview: false));
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Validation);
        Assert.That(await StoredMembersAsync(), Is.Empty);
    }

    // M05. Spec ACC-06. Only the Student role may join a group: Team 21 behavior, the specification
    // does not say whether teachers or assistants can be group members.
    [TestCase(CourseRole.Teacher)]
    [TestCase(CourseRole.Assistant)]
    public async Task AddMember_NonStudentCourseRole_ReturnsValidation(CourseRole role)
    {
        GivenMember(_student, _courseId, role);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Validation);
        Assert.That(await StoredMembersAsync(), Is.Empty);
    }

    // M06. Spec ACC-06. Related: IDN-03 (account status), read through a double.
    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Inactive)]
    public async Task AddMember_StudentAccountNotActive_ReturnsValidation(AccountStatus status)
    {
        _accounts.Set(_student, status);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Validation);
        Assert.That(await StoredMembersAsync(), Is.Empty);
        AssertGroupUnchanged(await StoredGroupAsync(_group.Id), _group);
    }

    // M07. Spec ACC-06; an archived group is frozen (Team 21 behavior)
    [Test]
    public async Task AddMember_ArchivedGroup_ReturnsConflict()
    {
        var archived = await SeedGroupAsync(_courseId, "Old group", GroupStatus.Archived);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, archived.Id, _student);

        AssertFailedWith(result, ErrorType.Conflict);
        Assert.That(await StoredMembersAsync(), Is.Empty);
        AssertGroupUnchanged(await StoredGroupAsync(archived.Id), archived);
    }

    // M08. Spec ACC-06 and ACC-01 (archiving stops new enrollments, keeps history). Course state comes
    // from the ICourseStateReader double; this is not the real ACC-01 adapter.
    [Test]
    public async Task AddMember_ArchivedCourse_ReturnsConflict()
    {
        _courseStates.Set(_courseId, CourseState.Archived);
        var earlierStudent = Guid.NewGuid();
        var history = await SeedMemberAsync(_group.Id, earlierStudent);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Conflict);
        var stored = await StoredMembersAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(history.Id));
            Assert.That(stored[0].UserId, Is.EqualTo(earlierStudent));
            Assert.That(stored[0].IsDeleted, Is.False);
            Assert.That(stored[0].UpdatedAt, Is.Null);
        });
        AssertGroupUnchanged(await StoredGroupAsync(_group.Id), _group);
    }

    // M09. Spec ACC-06. Related: SEC-01 (IDOR). The teacher is authorized for course A and the student
    // is enrolled there, so only the course/group pairing can cause the denial.
    [Test]
    public async Task AddMember_GroupFromAnotherCourse_ReturnsNotFound()
    {
        var otherCourse = Guid.NewGuid();
        _courseStates.Set(otherCourse, CourseState.Active);
        var foreign = await SeedGroupAsync(otherCourse, "Foreign group", GroupStatus.Active);
        await using var serviceDb = NewDb();
        var service = CreateService(serviceDb);

        var result = await service.AddMemberAsync(_courseId, foreign.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Error!.Message, Is.EqualTo("Group not found."));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Id.ToString()));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Name));
        Assert.That(await StoredMembersAsync(), Is.Empty, "no membership in either course");
        AssertGroupUnchanged(await StoredGroupAsync(foreign.Id), foreign);
    }

    // M10. Spec ACC-06. Related: SEC-01, 2.1 (denied privileged attempt audited). Course, group and
    // target are all valid, so only the caller's role can cause the denial: a student must not be able
    // to put anyone, themselves included, into a group that opens closed materials.
    [Test]
    public async Task AddMember_StudentCaller_ReturnsNotFound()
    {
        var classmate = Guid.NewGuid();
        GivenMember(classmate, _courseId, CourseRole.Student);
        await using var serviceDb = NewDb();
        var service = CreateService(classmate, serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Error!.Message, Is.EqualTo("Course not found."));
        Assert.That(await StoredMembersAsync(), Is.Empty);
        AssertGroupUnchanged(await StoredGroupAsync(_group.Id), _group);
        // Team 21: a denied add is audited against the group id
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.add", _group.Id, AuditOutcome.Denied)
        }));
    }

    // M11. Spec ACC-06. Related: SEC-01 (no caller, no change).
    [Test]
    public async Task AddMember_Anonymous_ReturnsUnauthorized()
    {
        await using var serviceDb = NewDb();
        var service = CreateService(callerId: null, serviceDb);

        var result = await service.AddMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Unauthorized);
        Assert.That(result.Error!.Message, Does.Not.Contain(_group.Id.ToString()));
        Assert.That(result.Error.Message, Does.Not.Contain(_group.Name));
        Assert.That(await StoredMembersAsync(), Is.Empty);
        AssertGroupUnchanged(await StoredGroupAsync(_group.Id), _group);
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    private GroupService CreateService(GroupsDbContext db) => CreateService(_teacher, db);

    private GroupService CreateService(Guid? callerId, GroupsDbContext db) =>
        new(db, new StubCurrentActor(callerId), _courseMembers, _accounts, _courseStates, _audit);

    private void GivenMember(Guid userId, Guid courseId, CourseRole role)
    {
        _accounts.Set(userId, AccountStatus.Active);
        _courseMembers.Set(userId, new CourseMembership(courseId, role, Active: true, CanReview: role == CourseRole.Teacher));
    }

    // Seeding and reading use their own contexts, so the service starts with nothing tracked and
    // persisted state is checked independently of what the service returned.
    private async Task<Group> SeedGroupAsync(Guid courseId, string name, GroupStatus status)
    {
        await using var db = NewDb();
        var group = new Group
        {
            CourseId = courseId,
            Name = name,
            Status = status,
            ArchivedAt = status == GroupStatus.Archived ? ArchivedAt : null,
            ArchivedBy = status == GroupStatus.Archived ? _teacher : null,
            CreatedBy = _teacher
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private async Task<GroupMember> SeedMemberAsync(Guid groupId, Guid userId)
    {
        await using var db = NewDb();
        var member = new GroupMember { GroupId = groupId, UserId = userId, CreatedBy = _teacher };
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    // Soft deleted rows are included, so a hidden remove or re-add would show up too
    private async Task<List<GroupMember>> StoredMembersAsync()
    {
        await using var db = NewDb();
        return await db.GroupMembers.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private async Task<Group> StoredGroupAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.Groups.IgnoreQueryFilters().AsNoTracking().SingleAsync(g => g.Id == id);
    }

    private static void AssertFailedWith(Result<GroupMemberDto> result, ErrorType type)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(type));
    }

    private static void AssertGroupUnchanged(Group stored, Group seeded)
    {
        Assert.Multiple(() =>
        {
            Assert.That(stored.CourseId, Is.EqualTo(seeded.CourseId));
            Assert.That(stored.Name, Is.EqualTo(seeded.Name));
            Assert.That(stored.Status, Is.EqualTo(seeded.Status));
            Assert.That(stored.ArchivedAt, Is.EqualTo(seeded.ArchivedAt));
            Assert.That(stored.IsDeleted, Is.False);
            Assert.That(stored.UpdatedAt, Is.Null);
        });
    }

    // Test doubles: one small class per interface GroupService depends on

    private sealed record AuditRecord(string Action, Guid ResourceId, AuditOutcome Outcome);

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditRecord> Records { get; } = new();

        public Task RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken ct = default)
        {
            Records.Add(new AuditRecord(action, resourceId, outcome));
            return Task.CompletedTask;
        }
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

        public void Remove(Guid accountId, Guid courseId) => _memberships.Remove((accountId, courseId));

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
