using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Modules.AssignmentsAccess;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// ASN-04 / ASN-06 TIM 15 (member 4: authorization)
// The real StartAttemptService (ASN-04) and the real ExtendDeadlineService (ASN-06) are wired to the
// real authorization adapters, the real platform access engine (DefaultDenyAccessDecision with the
// real course role policies) and the real membership check. Only the storage (assignments, attempts,
// deadline overrides, audit) is replaced by in-memory fakes. These tests prove that the services
// really use the authorization ports and stop at a denial, not only that the adapters work alone.
[TestFixture]
public sealed class AssignmentsAccessServiceIntegrationTests
{
    // ---- ASN-04: start an attempt through the real service ----

    [Test]
    [Property("RequirementId", "ASN-04")]
    public async Task StartAttempt_WhenActiveStudentOfTheCourse_StartsAnAttempt()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var attemptId = await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(f.Attempts.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    [Property("RequirementId", "ASN-04")]
    public void StartAttempt_WhenStudentOfAnotherCourse_IsDeniedAndNoAttemptIsCreated()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.OtherCourseId, CourseRole.Student);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Access denied."));
            Assert.That(f.Attempts.AttemptCount, Is.EqualTo(0));
        });
    }

    [Test]
    [Property("RequirementId", "ASN-04")]
    public void StartAttempt_WhenTeacherOfTheCourse_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(teacher), Guid.NewGuid()));

        Assert.That(f.Attempts.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    [Property("RequirementId", "ACC-03")]
    public async Task StartAttempt_WhenMembershipIsRemovedBetweenCalls_TheNextCallIsDenied()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid());
        f.AddMember(student, f.CourseId, CourseRole.Student, MembershipStatus.Left);

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid()));
        Assert.That(f.Attempts.AttemptCount, Is.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ASN-04")]
    public async Task StartAttempt_WhenAuthorizedButTheLimitIsReached_IsRejectedWithoutANewAttempt()
    {
        var f = new Fixture(maxAttempts: 1);
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.StartService.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Maximum number of attempts reached."));
            Assert.That(f.Attempts.AttemptCount, Is.EqualTo(1));
        });
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void StartAttempt_WhenTheAccessEngineTimesOutInternally_IsDeniedInsteadOfFailingOpen()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);
        var service = new StartAttemptService(
            f.Repository,
            f.Attempts,
            new AccessStartAttemptAuthorization(
                new ThrowingEngine(new OperationCanceledException("engine timeout")),
                f.Accounts));

        // The caller's token is not cancelled, so this is a dependency timeout, not a cancellation.
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await service.StartAttemptAsync(f.Assignment.Id, Id(student), Guid.NewGuid()));
        Assert.That(f.Attempts.AttemptCount, Is.EqualTo(0));
    }

    // ---- ASN-06: extend a deadline through the real service ----

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task ExtendDeadline_WhenTeacherOfTheCourse_SavesTheIndividualDeadline()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        var student = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);
        f.AddMember(student, f.CourseId, CourseRole.Student);
        var newDue = f.Due.AddDays(3);

        var result = await f.ExtendService.ExtendDeadlineAsync(
            Id(teacher), f.Assignment.Id, Id(student), newDue);

        var stored = await f.Store.GetAsync(f.Assignment.Id, Id(student));
        Assert.Multiple(() =>
        {
            Assert.That(result.DueAtUtc, Is.EqualTo(newDue));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.DueAtUtc, Is.EqualTo(newDue));
            Assert.That(f.Store.SavedAudit, Has.Count.EqualTo(1));
            Assert.That(f.Store.SavedAudit[0].Outcome, Is.EqualTo(DeadlineExtensionAuditOutcome.Success));
        });
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public void ExtendDeadline_WhenStudentExtendsTheirOwnDeadline_IsDeniedAndBothAuditsRecordIt()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.ExtendService.ExtendDeadlineAsync(
                Id(student), f.Assignment.Id, Id(student), f.Due.AddDays(3)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Access denied."));
            Assert.That(f.Store.SaveCount, Is.EqualTo(0));

            // The assignments audit (the service records the refusal).
            Assert.That(f.ExtensionAudit.Entries, Has.Count.EqualTo(1));
            Assert.That(f.ExtensionAudit.Entries[0].Outcome, Is.EqualTo(DeadlineExtensionAuditOutcome.Denied));
            Assert.That(f.ExtensionAudit.Entries[0].ReasonCode, Is.EqualTo(DeadlineExtensionReasonCodes.Forbidden));

            // The access audit (the engine records the decision).
            Assert.That(
                f.AccessAudit.Entries.Any(entry =>
                    entry.Decision.Outcome == AccessOutcome.Deny &&
                    entry.Request.Action == AccessActions.AssignmentManage.Id),
                Is.True);
        });
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public void ExtendDeadline_WhenTeacherOfAnotherCourse_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        var student = f.AddAccount();
        f.AddMember(teacher, f.OtherCourseId, CourseRole.Teacher);
        f.AddMember(student, f.CourseId, CourseRole.Student);

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.ExtendService.ExtendDeadlineAsync(
                Id(teacher), f.Assignment.Id, Id(student), f.Due.AddDays(3)));

        Assert.That(f.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public void ExtendDeadline_WhenAssistantHasTheActionByDelegation_IsStillDenied()
    {
        var f = new Fixture();
        var assistant = f.AddAccount();
        var student = f.AddAccount();
        f.AddMember(
            assistant, f.CourseId, CourseRole.Assistant, MembershipStatus.Active, AccessActions.AssignmentManage.Id);
        f.AddMember(student, f.CourseId, CourseRole.Student);

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.ExtendService.ExtendDeadlineAsync(
                Id(assistant), f.Assignment.Id, Id(student), f.Due.AddDays(3)));

        Assert.That(f.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    [Property("RequirementId", "ACC-02")]
    public void ExtendDeadline_WhenAuthorizedButTheStudentIsNotAnActiveMember_FailsWithoutSaving()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        var student = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);
        f.AddMember(student, f.CourseId, CourseRole.Student, MembershipStatus.Left);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.ExtendService.ExtendDeadlineAsync(
                Id(teacher), f.Assignment.Id, Id(student), f.Due.AddDays(3)));

        Assert.Multiple(() =>
        {
            Assert.That(f.Store.SaveCount, Is.EqualTo(0));
            Assert.That(f.ExtensionAudit.Entries, Has.Count.EqualTo(1));
            Assert.That(f.ExtensionAudit.Entries[0].Outcome, Is.EqualTo(DeadlineExtensionAuditOutcome.Failure));
            Assert.That(f.ExtensionAudit.Entries[0].ReasonCode, Is.EqualTo(DeadlineExtensionReasonCodes.NotEnrolled));
        });
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ExtendDeadline_WhenTheAccessEngineTimesOutInternally_IsDeniedInsteadOfFailingOpen()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        var student = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);
        f.AddMember(student, f.CourseId, CourseRole.Student);
        var service = new ExtendDeadlineService(
            f.Repository,
            new AccessDeadlineExtensionAuthorization(
                new ThrowingEngine(new OperationCanceledException("engine timeout")),
                f.Accounts),
            new MembershipCourseEnrollmentCheck(f.Memberships),
            f.Store,
            f.ExtensionAudit,
            TimeProvider.System);

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await service.ExtendDeadlineAsync(Id(teacher), f.Assignment.Id, Id(student), f.Due.AddDays(3)));
        Assert.That(f.Store.SaveCount, Is.EqualTo(0));
    }

    // ---- helpers ----

    private static string Id(Guid account) => account.ToString("D");

    private sealed class Fixture
    {
        public Fixture(int maxAttempts = 2)
        {
            var now = DateTimeOffset.UtcNow;
            Due = now.AddDays(2);

            // The course ID of an assignment is the course GUID in "D" format, because the access
            // module identifies courses with a GUID (see the open point in the component document).
            Assignment = new Assignment(
                Guid.NewGuid(),
                CourseId.ToString("D"),
                "revision-1",
                now.AddDays(-1),
                Due,
                maxAttempts);

            Repository = new FakeAssignmentRepository(Assignment);
            Attempts = new FakeAttemptStore();

            var resources = new InMemoryResourceDescriptorResolver();
            resources.SetCourse(new ResourceRef("assignment", Assignment.Id.ToString("D")), CourseId);

            var registry = new InMemoryAccessPolicyRegistry();
            foreach (var definition in CourseRolePolicies.All)
            {
                registry.Register(
                    definition.Action.Id,
                    new CourseRoleAccessPolicy(definition, Memberships, resources));
            }

            var engine = new DefaultDenyAccessDecision(
                new InMemoryActionCatalog(),
                registry,
                AccessAudit,
                new AccessDecisionOptions());

            StartService = new StartAttemptService(
                Repository,
                Attempts,
                new AccessStartAttemptAuthorization(engine, Accounts));

            ExtendService = new ExtendDeadlineService(
                Repository,
                new AccessDeadlineExtensionAuthorization(engine, Accounts),
                new MembershipCourseEnrollmentCheck(Memberships),
                Store,
                ExtensionAudit,
                TimeProvider.System);
        }

        public Guid CourseId { get; } = Guid.NewGuid();
        public Guid OtherCourseId { get; } = Guid.NewGuid();
        public DateTimeOffset Due { get; }
        public Assignment Assignment { get; }

        public InMemoryMembershipReader Memberships { get; } = new();
        public FakeAccounts Accounts { get; } = new();
        public RecordingAccessAudit AccessAudit { get; } = new();
        public IAssignmentRepository Repository { get; }
        public FakeAttemptStore Attempts { get; }
        public FakeDeadlineExtensionStore Store { get; } = new();
        public FakeDeadlineExtensionAuditWriter ExtensionAudit { get; } = new();

        public StartAttemptService StartService { get; }
        public ExtendDeadlineService ExtendService { get; }

        public Guid AddAccount(AccountState state = AccountState.Active)
        {
            var id = Guid.NewGuid();
            Accounts.Set(id, state);
            return id;
        }

        public void AddMember(
            Guid account,
            Guid course,
            CourseRole role,
            MembershipStatus status = MembershipStatus.Active,
            params string[] delegatedActions) =>
            Memberships.Set(new CourseMembership(
                account,
                course,
                role,
                status,
                new HashSet<string>(delegatedActions, StringComparer.Ordinal)));
    }

    private sealed class FakeAccounts : IAccountStateReader
    {
        private readonly Dictionary<Guid, AccountState> states = new();

        public void Set(Guid id, AccountState state) => states[id] = state;

        public ValueTask<AccountState> GetStateAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(states.TryGetValue(accountId, out var state) ? state : AccountState.Unknown);
    }

    private sealed class RecordingAccessAudit : IAccessAuditWriter
    {
        public List<(AccessRequest Request, AccessDecision Decision)> Entries { get; } = new();

        public ValueTask WriteAsync(
            AccessRequest request,
            AccessDecision decision,
            CancellationToken cancellationToken = default)
        {
            Entries.Add((request, decision));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingEngine(Exception exception) : IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default) => throw exception;
    }

    private sealed class FakeAssignmentRepository(Assignment assignment) : IAssignmentRepository
    {
        public ValueTask<Assignment?> GetByIdAsync(
            Guid assignmentId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Assignment?>(assignmentId == assignment.Id ? assignment : null);

        public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
            IReadOnlyCollection<string> courseIds,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask AddAsync(
            Assignment assignment,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask UpdateAsync(
            Assignment assignment,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeAttemptStore : IAttemptStore
    {
        public int AttemptCount { get; private set; }

        public ValueTask<Guid?> TryStartAsync(
            Guid assignmentId,
            string studentId,
            string publishedLabRevisionId,
            int maxAttempts,
            Guid requestId,
            CancellationToken cancellationToken = default)
        {
            if (AttemptCount >= maxAttempts)
            {
                return ValueTask.FromResult<Guid?>(null);
            }

            AttemptCount++;
            return ValueTask.FromResult<Guid?>(Guid.NewGuid());
        }
    }

    private sealed class FakeDeadlineExtensionAuditWriter : IDeadlineExtensionAuditWriter
    {
        public List<DeadlineExtensionAuditEntry> Entries { get; } = [];

        public ValueTask<bool> TryWriteAsync(
            DeadlineExtensionAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakeDeadlineExtensionStore : IDeadlineExtensionStore
    {
        private readonly Dictionary<(Guid, string), StudentDeadlineOverride> overrides = new();

        public List<DeadlineExtensionAuditEntry> SavedAudit { get; } = [];

        public int SaveCount { get; private set; }

        public ValueTask<StudentDeadlineOverride?> GetAsync(
            Guid assignmentId,
            string studentId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(overrides.GetValueOrDefault((assignmentId, studentId)));

        public ValueTask SaveAsync(
            StudentDeadlineOverride deadlineOverride,
            DeadlineExtensionAuditEntry auditEntry,
            CancellationToken cancellationToken = default)
        {
            overrides[(deadlineOverride.AssignmentId, deadlineOverride.StudentId)] = deadlineOverride;
            SavedAudit.Add(auditEntry);
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }
}
