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
/// ACC-06 group creation and teacher-only access, through the real <see cref="GroupService"/> and the
/// real <see cref="GroupsDbContext"/> on the EF in memory provider.
/// <para>
/// In memory has no unique indexes, foreign keys or Postgres errors, so a conflict here comes from the
/// service's own check, not from the database. Account status, course membership and course state are
/// small doubles of the other teams' readers (IDN-03, ACC-02/03, ACC-01), not their real adapters.
/// </para>
/// <para>
/// Spec SEC-01 / 2.1: authorization on the server, denial without revealing the resource, privileged
/// actions audited. Team 21 implementation: every denial of a signed-in caller is NotFound
/// "Course not found." and is audited as Denied; an anonymous caller gets Unauthorized.
/// </para>
/// </summary>
public class GroupServiceCreateTests
{
    private Guid _courseId;
    private Guid _teacher;

    private DbContextOptions<GroupsDbContext> _dbOptions = null!;
    private AccountStatuses _accounts = null!;
    private CourseMemberships _courseMembers = null!;
    private CourseStates _courseStates = null!;
    private RecordingAuditTrail _audit = null!;

    [SetUp]
    public void FreshWorld()
    {
        _courseId = Guid.NewGuid();
        _teacher = Guid.NewGuid();

        // One in memory database per test, named by a fresh guid
        _dbOptions = new DbContextOptionsBuilder<GroupsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _accounts = new AccountStatuses();
        _courseMembers = new CourseMemberships();
        _courseStates = new CourseStates();
        _audit = new RecordingAuditTrail();

        _courseStates.Set(_courseId, CourseState.Active);
    }

    // G01. Spec ACC-06 (positive), 2.1 / AUD-01 (privileged action is audited)
    [Test]
    public async Task CreateGroup_ActiveTeacher_CreatesGroup()
    {
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group A");

        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(dto.CourseId, Is.EqualTo(_courseId));
            Assert.That(dto.Name, Is.EqualTo("Group A"));
            Assert.That(dto.Status, Is.EqualTo("Active"));
        });

        var stored = await StoredGroupsAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].Id, Is.EqualTo(dto.Id));
            Assert.That(stored[0].CourseId, Is.EqualTo(_courseId));
            Assert.That(stored[0].Name, Is.EqualTo("Group A"));
            Assert.That(stored[0].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(stored[0].CreatedBy, Is.EqualTo(_teacher));
            Assert.That(stored[0].IsDeleted, Is.False);
        });

        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.create", dto.Id, AuditOutcome.Success)
        }));
    }

    // G02. Spec SEC-01, ACC-06: only the course's teacher manages groups
    [Test]
    public async Task CreateGroup_Student_ReturnsNotFound()
    {
        var student = Guid.NewGuid();
        GivenActiveMember(student, _courseId, CourseRole.Student);
        await using var serviceDb = NewDb();
        var service = CreateService(student, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group A");

        AssertCourseNotFound(result);
        Assert.That(await StoredGroupsAsync(), Is.Empty);
        // Team 21: a denied signed-in caller is audited against the course id
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.create", _courseId, AuditOutcome.Denied)
        }));
    }

    // G03. Spec SEC-01: no caller, no change
    [Test]
    public async Task CreateGroup_Anonymous_ReturnsUnauthorized()
    {
        await using var serviceDb = NewDb();
        var service = CreateService(userId: null, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group A");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Unauthorized));
        Assert.That(result.Error.Message, Does.Not.Contain(_courseId.ToString()));
        Assert.That(await StoredGroupsAsync(), Is.Empty);
    }

    // G04. Spec IDN-03 (blocked account), SEC-01. The teacher role alone is not enough.
    [Test]
    public async Task CreateGroup_BlockedTeacher_ReturnsNotFound()
    {
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        _accounts.Set(_teacher, AccountStatus.Blocked);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group A");

        AssertCourseNotFound(result);
        Assert.That(await StoredGroupsAsync(), Is.Empty);
        Assert.That(_audit.Records, Is.EqualTo(new[]
        {
            new AuditRecord("group.create", _courseId, AuditOutcome.Denied)
        }));
    }

    // G05. Team 21 rule: names are unique among active groups of a course. Service check only,
    // in memory does not enforce the Postgres filtered unique index.
    [Test]
    public async Task CreateGroup_DuplicateActiveName_ReturnsConflict()
    {
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        var existing = await SeedGroupAsync(_courseId, "Group A");
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group A");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Conflict));
        var stored = await StoredGroupsAsync();
        Assert.That(stored.Select(g => g.Id), Is.EqualTo(new[] { existing.Id }));
    }

    // G06. Spec SEC-05 asks for input validation; the exact rules (required, no surrounding spaces,
    // at most 100 characters, no control characters) are Team 21 implementation behavior.
    [TestCaseSource(nameof(InvalidNames))]
    public async Task CreateGroup_InvalidName_ReturnsValidation(string name)
    {
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, name);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(await StoredGroupsAsync(), Is.Empty);
    }

    private static IEnumerable<TestCaseData> InvalidNames()
    {
        yield return new TestCaseData("").SetArgDisplayNames("empty");
        yield return new TestCaseData("   ").SetArgDisplayNames("whitespace only");
        yield return new TestCaseData(" Group A").SetArgDisplayNames("leading space");
        yield return new TestCaseData("Group A ").SetArgDisplayNames("trailing space");
        yield return new TestCaseData(new string('a', Group.NameMaxLength + 1)).SetArgDisplayNames("101 characters");
        yield return new TestCaseData("Group\nA").SetArgDisplayNames("newline");
        yield return new TestCaseData("Group\tA").SetArgDisplayNames("tab");
        yield return new TestCaseData("Group\0A").SetArgDisplayNames("null character");
    }

    // G07. Spec ACC-01: archiving stops new enrollments and keeps history. Team 21 applies it to groups
    // through the ICourseStateReader contract; this is not the real ACC-01 database adapter.
    [Test]
    public async Task CreateGroup_ArchivedCourse_ReturnsConflict()
    {
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        _courseStates.Set(_courseId, CourseState.Archived);
        var existing = await SeedGroupAsync(_courseId, "Group A");
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.CreateGroupAsync(_courseId, "Group B");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.Conflict));
        var stored = await StoredGroupsAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        AssertUnchanged(stored[0], existing);
    }

    // G08. Spec SEC-01 (IDOR): a group id from another course is refused even for an authorized teacher
    [Test]
    public async Task GetGroup_GroupBelongsToAnotherCourse_ReturnsNotFound()
    {
        var otherCourse = Guid.NewGuid();
        GivenActiveMember(_teacher, _courseId, CourseRole.Teacher);
        var foreign = await SeedGroupAsync(otherCourse, "Foreign group");
        await using var serviceDb = NewDb();
        var service = CreateService(_teacher, serviceDb);

        var result = await service.GetGroupAsync(_courseId, foreign.Id);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(result.Error.Message, Is.EqualTo("Group not found."));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Id.ToString()));
        Assert.That(result.Error.Message, Does.Not.Contain(foreign.Name));
        var stored = await StoredGroupsAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        AssertUnchanged(stored[0], foreign);
    }

    private GroupsDbContext NewDb() => new(_dbOptions);

    private GroupService CreateService(Guid? userId, GroupsDbContext db) =>
        new(db, new StubCurrentActor(userId), _courseMembers, _accounts, _courseStates, _audit);

    private void GivenActiveMember(Guid userId, Guid courseId, CourseRole role)
    {
        _accounts.Set(userId, AccountStatus.Active);
        _courseMembers.Set(userId, new CourseMembership(courseId, role, Active: true, CanReview: role == CourseRole.Teacher));
    }

    // Seeds through its own context, so the service starts with nothing tracked
    private async Task<Group> SeedGroupAsync(Guid courseId, string name)
    {
        await using var db = NewDb();
        var group = new Group { CourseId = courseId, Name = name, CreatedBy = Guid.NewGuid() };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    // Reads through a fresh context, independent of what the service returned or tracked.
    // Soft deleted rows are included so a hidden delete would show up too.
    private async Task<List<Group>> StoredGroupsAsync()
    {
        await using var db = NewDb();
        return await db.Groups.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private static void AssertCourseNotFound(Result<GroupDto> result)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error!.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(result.Error.Message, Is.EqualTo("Course not found."));
    }

    private static void AssertUnchanged(Group stored, Group seeded)
    {
        Assert.Multiple(() =>
        {
            Assert.That(stored.Id, Is.EqualTo(seeded.Id));
            Assert.That(stored.CourseId, Is.EqualTo(seeded.CourseId));
            Assert.That(stored.Name, Is.EqualTo(seeded.Name));
            Assert.That(stored.Status, Is.EqualTo(GroupStatus.Active));
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
