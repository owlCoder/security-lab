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
/// ACC-06 removing a student from a group, and the membership history it leaves, through the real
/// <see cref="GroupService"/> and the real <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// Spec 2.1: a student leaving must not retroactively change history; denials happen on the server.
/// Team 21 implementation: removal is a soft delete (IsDeleted, DeletedAt, DeletedBy), re-joining adds
/// a new row, and members of an archived group or course are frozen.
/// </para>
/// <para>
/// Every test starts from a world where the removal would succeed (R01) and changes one condition.
/// Account status, course membership and course state are doubles of the other modules' readers.
/// In memory enforces no unique or filtered index; this proves the service, not Postgres.
/// </para>
/// </summary>
public class GroupServiceRemoveMemberTests
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

    // R01. Spec ACC-06; 2.1 (history kept, privileged action audited). Soft delete is Team 21 behavior.
    [Test]
    public async Task RemoveMember_ValidMember_SoftDeletesAndPreservesHistory()
    {
        var seeded = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);
        var before = DateTime.UtcNow;

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        var after = DateTime.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(await ActiveMemberIdsAsync(), Is.Empty, "normal reads leave the removed member out");
        Assert.That(await ListedMemberUserIdsAsync(), Is.Empty, "GetMembersAsync leaves the removed member out");

        var stored = await AllMembersAsync();
        Assert.That(stored, Has.Count.EqualTo(1), "the row stays as history");
        var row = stored[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.EqualTo(seeded.Id));
            Assert.That(row.IsDeleted, Is.True);
            Assert.That(row.DeletedAt, Is.InRange(before, after));
            Assert.That(row.DeletedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(row.DeletedBy, Is.EqualTo(_teacher));
            Assert.That(row.UpdatedBy, Is.EqualTo(_teacher));
            Assert.That(row.CreatedAt, Is.EqualTo(seeded.CreatedAt));
            Assert.That(row.CreatedBy, Is.EqualTo(seeded.CreatedBy));
        });

        // Team 21: the audit resource is the group id (the interface takes a single id)
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.remove", _group.Id, AuditOutcome.Success)
        }));
    }

    // R02. Spec ACC-06, 2.1. A second removal must not touch the history of the first.
    [Test]
    public async Task RemoveMember_AlreadyRemoved_ReturnsNotFound()
    {
        await SeedMemberAsync(_group.Id, _student);
        await using (var firstDb = NewDb())
        {
            var first = await CreateService(_teacher, firstDb).RemoveMemberAsync(_courseId, _group.Id, _student);
            Assert.That(first.IsSuccess, Is.True, "precondition: the first removal succeeds");
        }
        var afterFirst = (await AllMembersAsync()).Single();
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        var stored = await AllMembersAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(afterFirst.Id));
            Assert.That(stored[0].IsDeleted, Is.True);
            Assert.That(stored[0].DeletedAt, Is.EqualTo(afterFirst.DeletedAt));
            Assert.That(stored[0].DeletedBy, Is.EqualTo(afterFirst.DeletedBy));
            Assert.That(stored[0].UpdatedAt, Is.EqualTo(afterFirst.UpdatedAt));
        });
        Assert.That(_audit.Records, Has.Count.EqualTo(1), "only the first removal is audited");
    }

    // R03. Spec ACC-06
    [Test]
    public async Task RemoveMember_NonexistentMember_ReturnsNotFound()
    {
        var otherMember = await SeedMemberAsync(_group.Id, Guid.NewGuid());
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        AssertOnlyUnchangedMember(await AllMembersAsync(), otherMember);
    }

    // R04. Spec ACC-06. Related: SEC-01 (no caller, no change).
    [Test]
    public async Task RemoveMember_Anonymous_ReturnsUnauthorized()
    {
        var seeded = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(userId: null, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Unauthorized);
        AssertOnlyUnchangedMember(await AllMembersAsync(), seeded);
    }

    // R05. Spec ACC-06. Related: SEC-01, 2.1 (denied privileged attempt audited).
    [Test]
    public async Task RemoveMember_StudentCaller_ReturnsNotFound()
    {
        var classmate = Guid.NewGuid();
        GivenMember(classmate, _courseId, CourseRole.Student);
        var seeded = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(classmate, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Error!.Message, Is.EqualTo("Course not found."));
        AssertOnlyUnchangedMember(await AllMembersAsync(), seeded);
        // Team 21: a denied remove is audited against the group id
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.remove", _group.Id, AuditOutcome.Denied)
        }));
    }

    // R06. Spec ACC-06. Related: IDN-03 (account status, read through a double).
    [Test]
    public async Task RemoveMember_BlockedTeacher_ReturnsNotFound()
    {
        _accounts.Set(_teacher, AccountStatus.Blocked);
        var seeded = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        AssertOnlyUnchangedMember(await AllMembersAsync(), seeded);
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.remove", _group.Id, AuditOutcome.Denied)
        }));
    }

    // R07. Spec ACC-06. Related: SEC-01 (IDOR). The teacher is authorized for course A, so only the
    // course/group pairing can cause the denial.
    [Test]
    public async Task RemoveMember_GroupBelongsToAnotherCourse_ReturnsNotFound()
    {
        var otherCourse = Guid.NewGuid();
        _courseStates.Set(otherCourse, CourseState.Active);
        var foreign = await SeedGroupAsync(otherCourse, "Foreign group", GroupStatus.Active);
        var foreignMember = await SeedMemberAsync(foreign.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, foreign.Id, _student);

        AssertFailedWith(result, ErrorType.NotFound);
        Assert.That(result.Error!.Message, Is.EqualTo("Group not found."));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Id.ToString()));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Name));
        AssertOnlyUnchangedMember(await AllMembersAsync(), foreignMember);
    }

    // R08. Spec ACC-06. Freezing the members of an archived group is Team 21 behavior.
    [Test]
    public async Task RemoveMember_ArchivedGroup_ReturnsConflict()
    {
        var archived = await SeedGroupAsync(_courseId, "Old group", GroupStatus.Archived);
        var seeded = await SeedMemberAsync(archived.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, archived.Id, _student);

        AssertFailedWith(result, ErrorType.Conflict);
        AssertOnlyUnchangedMember(await AllMembersAsync(), seeded);
    }

    // R09. Spec ACC-06. Related: ACC-01 (archiving keeps history). That an archived course also blocks
    // removals is Team 21 behavior; ACC-01 only names new enrollments. Course state comes from the
    // ICourseStateReader double, not the real ACC-01 adapter.
    [Test]
    public async Task RemoveMember_ArchivedCourse_ReturnsConflict()
    {
        _courseStates.Set(_courseId, CourseState.Archived);
        var seeded = await SeedMemberAsync(_group.Id, _student);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.RemoveMemberAsync(_courseId, _group.Id, _student);

        AssertFailedWith(result, ErrorType.Conflict);
        Assert.That(result.Error!.Message, Is.EqualTo("Course is archived."));
        AssertOnlyUnchangedMember(await AllMembersAsync(), seeded);
    }

    // R10. Spec ACC-06, 2.1 (history kept). A GroupService lifecycle test, not an ACC-02 enrollment test.
    // Each step uses its own context, like separate requests.
    [Test]
    public async Task RemoveMember_ThenReAdd_CreatesNewActiveRowAndPreservesOldRow()
    {
        Result<GroupMemberDto> firstAdd, secondAdd;
        Result removal;

        await using (var db = NewDb())
            firstAdd = await CreateService(_teacher, db).AddMemberAsync(_courseId, _group.Id, _student);
        await using (var db = NewDb())
            removal = await CreateService(_teacher, db).RemoveMemberAsync(_courseId, _group.Id, _student);
        await using (var db = NewDb())
            secondAdd = await CreateService(_teacher, db).AddMemberAsync(_courseId, _group.Id, _student);

        Assert.That(firstAdd.IsSuccess, Is.True);
        Assert.That(removal.IsSuccess, Is.True);
        Assert.That(secondAdd.IsSuccess, Is.True);
        var oldId = firstAdd.Value!.Id;
        var newId = secondAdd.Value!.Id;
        Assert.That(newId, Is.Not.EqualTo(oldId));

        Assert.That(await ActiveMemberIdsAsync(), Is.EqualTo(new[] { newId }), "exactly one active membership");
        Assert.That(await ListedMemberUserIdsAsync(), Is.EqualTo(new[] { _student }));

        var stored = await AllMembersAsync();
        Assert.That(stored.Select(m => m.Id), Is.EquivalentTo(new[] { oldId, newId }), "both rows stay");
        var oldRow = stored.Single(m => m.Id == oldId);
        var newRow = stored.Single(m => m.Id == newId);
        Assert.Multiple(() =>
        {
            Assert.That(oldRow.IsDeleted, Is.True);
            Assert.That(oldRow.CreatedAt, Is.EqualTo(firstAdd.Value.JoinedAt));
            Assert.That(oldRow.CreatedBy, Is.EqualTo(_teacher));
            Assert.That(oldRow.DeletedAt, Is.Not.Null);
            Assert.That(oldRow.DeletedBy, Is.EqualTo(_teacher));
            Assert.That(newRow.IsDeleted, Is.False);
            Assert.That(newRow.DeletedAt, Is.Null);
            Assert.That(newRow.UserId, Is.EqualTo(_student));
        });

        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.member.add", _group.Id, AuditOutcome.Success),
            new AuditRecord("group.member.remove", _group.Id, AuditOutcome.Success),
            new AuditRecord("group.member.add", _group.Id, AuditOutcome.Success)
        }));
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    private GroupService CreateService(Guid? userId, GroupsDbContext db) =>
        new(db, new StubCurrentActor(userId), _courseMembers, _accounts, _courseStates, _audit);

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

    // Normal read: the soft delete filter applies
    private async Task<List<Guid>> ActiveMemberIdsAsync()
    {
        await using var db = NewDb();
        return await db.GroupMembers.AsNoTracking().Select(m => m.Id).ToListAsync();
    }

    // History read: only the soft delete filter is bypassed
    private async Task<List<GroupMember>> AllMembersAsync()
    {
        await using var db = NewDb();
        return await db.GroupMembers
            .IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])
            .AsNoTracking()
            .ToListAsync();
    }

    // The service's own read of current members, as the teacher would see it
    private async Task<List<Guid>> ListedMemberUserIdsAsync()
    {
        await using var db = NewDb();
        var members = await CreateService(_teacher, db).GetMembersAsync(_courseId, _group.Id);
        Assert.That(members.IsSuccess, Is.True, "precondition: the teacher can list members");
        return members.Value!.Select(m => m.UserId).ToList();
    }

    private static void AssertFailedWith(Result result, ErrorType type)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(type));
    }

    // Exactly one stored row, still active and never updated or deleted
    private static void AssertOnlyUnchangedMember(List<GroupMember> stored, GroupMember seeded)
    {
        Assert.That(stored, Has.Count.EqualTo(1));
        var row = stored[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.EqualTo(seeded.Id));
            Assert.That(row.GroupId, Is.EqualTo(seeded.GroupId));
            Assert.That(row.UserId, Is.EqualTo(seeded.UserId));
            Assert.That(row.IsDeleted, Is.False);
            Assert.That(row.DeletedAt, Is.Null);
            Assert.That(row.DeletedBy, Is.Null);
            Assert.That(row.UpdatedAt, Is.Null);
            Assert.That(row.UpdatedBy, Is.Null);
            Assert.That(row.CreatedAt, Is.EqualTo(seeded.CreatedAt));
            Assert.That(row.CreatedBy, Is.EqualTo(seeded.CreatedBy));
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
