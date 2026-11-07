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

namespace Platform.Tests.Assignments;

// ASN-04 / ASN-06 TIM 15 (member 4: authorization)
// These tests use the real platform access engine (DefaultDenyAccessDecision with the real course
// role policies) and in-memory membership and resource data, so they prove the adapters and the
// engine work together. They do not prove the database, the real identity store or the HTTP layer.
[TestFixture]
public sealed class AssignmentsAccessAdapterTests
{
    // ---- ASN-06: who may extend a deadline ----

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task CanExtend_WhenActiveTeacherOfTheCourse_IsAllowed()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var allowed = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.True);
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task CanExtend_WhenStudentTriesToExtendOwnDeadline_IsDenied()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var allowed = await f.Extension.CanExtendAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task CanExtend_WhenTeacherOfAnotherCourse_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.OtherCourseId, CourseRole.Teacher);

        var allowed = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task CanExtend_UsesTheCourseOfTheAssignment_NotTheCourseIdArgument()
    {
        var f = new Fixture();
        var outsider = f.AddAccount();
        f.AddMember(outsider, f.OtherCourseId, CourseRole.Teacher);
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        // A caller that names the outsider's own course must not gain access to another course's assignment.
        var outsiderAllowed = await f.Extension.CanExtendAsync(
            Id(outsider), f.AssignmentId, f.OtherCourseId.ToString());
        // A wrong or missing course value must not take access away from the real teacher either.
        var teacherAllowed = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, "not-a-guid");

        Assert.Multiple(() =>
        {
            Assert.That(outsiderAllowed, Is.False);
            Assert.That(teacherAllowed, Is.True);
        });
    }

    [Test]
    [Property("RequirementId", "ASN-06")]
    public async Task CanExtend_WhenAssistantEvenWithDelegatedAction_IsDenied()
    {
        var f = new Fixture();
        var assistant = f.AddAccount();
        f.AddMember(assistant, f.CourseId, CourseRole.Assistant, MembershipStatus.Active, AccessActions.AssignmentManage.Id);

        var allowed = await f.Extension.CanExtendAsync(Id(assistant), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-03")]
    public async Task CanExtend_WhenTeacherMembershipIsRemoved_IsDeniedOnTheNextRequest()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var before = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());
        f.Memberships.Remove(teacher, f.CourseId);
        var after = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.True);
            Assert.That(after, Is.False);
        });
    }

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    [TestCase(AccountState.Unknown)]
    [Property("RequirementId", "IDN-03")]
    public async Task CanExtend_WhenAccountIsNotActive_IsDenied(AccountState state)
    {
        var f = new Fixture();
        var teacher = f.AddAccount(state);
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var allowed = await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task CanExtend_WhenAssignmentIsUnknown_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var allowed = await f.Extension.CanExtendAsync(Id(teacher), Guid.NewGuid(), f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("teacher-1")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [Property("RequirementId", "ACC-05")]
    public async Task CanExtend_WhenActorIdIsNotAnAccountId_IsDenied(string actorId)
    {
        var f = new Fixture();

        var allowed = await f.Extension.CanExtendAsync(actorId, f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task CanExtend_WhenAssignmentIdIsEmpty_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var allowed = await f.Extension.CanExtendAsync(Id(teacher), Guid.Empty, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    // ---- ASN-04: who may start an attempt ----

    [Test]
    [Property("RequirementId", "ASN-04")]
    public async Task CanStart_WhenActiveStudentOfTheCourse_IsAllowed()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var allowed = await f.StartAttempt.CanStartAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.True);
    }

    [Test]
    [Property("RequirementId", "ACC-04")]
    public async Task CanStart_WhenStudentOfAnotherCourse_IsDenied()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.OtherCourseId, CourseRole.Student);

        var allowed = await f.StartAttempt.CanStartAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ASN-04")]
    public async Task CanStart_WhenUserIsNotAMemberAtAll_IsDenied()
    {
        var f = new Fixture();
        var stranger = f.AddAccount();

        var allowed = await f.StartAttempt.CanStartAsync(Id(stranger), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ASN-04")]
    public async Task CanStart_WhenTeacher_IsDenied()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        var allowed = await f.StartAttempt.CanStartAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [TestCase(MembershipStatus.Left)]
    [TestCase(MembershipStatus.Suspended)]
    [Property("RequirementId", "ACC-02")]
    public async Task CanStart_WhenMembershipIsNotActive_IsDenied(MembershipStatus status)
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student, status);

        var allowed = await f.StartAttempt.CanStartAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "IDN-03")]
    public async Task CanStart_WhenStudentAccountIsBlocked_IsDenied()
    {
        var f = new Fixture();
        var student = f.AddAccount(AccountState.Blocked);
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var allowed = await f.StartAttempt.CanStartAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task CanStart_WhenAssignmentIsUnknown_IsDenied()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var allowed = await f.StartAttempt.CanStartAsync(Id(student), Guid.NewGuid(), f.CourseId.ToString());

        Assert.That(allowed, Is.False);
    }

    // ---- fail closed ----

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task Adapters_WhenTheAccessEngineFails_DenyInsteadOfAllowing()
    {
        var f = new Fixture();
        var account = f.AddAccount();
        var broken = new ThrowingEngine(new InvalidOperationException("engine down"));
        var extension = new AccessDeadlineExtensionAuthorization(broken, f.Accounts);
        var start = new AccessStartAttemptAuthorization(broken, f.Accounts);

        var canExtend = await extension.CanExtendAsync(Id(account), f.AssignmentId, f.CourseId.ToString());
        var canStart = await start.CanStartAsync(Id(account), f.AssignmentId, f.CourseId.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(canExtend, Is.False);
            Assert.That(canStart, Is.False);
        });
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void Adapters_WhenTheRequestIsCancelled_PassTheCancellationOn()
    {
        var f = new Fixture();
        var account = f.AddAccount();
        var cancelling = new ThrowingEngine(new OperationCanceledException());
        var extension = new AccessDeadlineExtensionAuthorization(cancelling, f.Accounts);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await extension.CanExtendAsync(Id(account), f.AssignmentId, f.CourseId.ToString()));
    }

    // ---- audit of access decisions (the engine writes it; the adapters must reach the engine) ----

    [Test]
    [Property("RequirementId", "AUD-01")]
    public async Task DeniedExtension_IsAuditedWithActorActionAndResource()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        await f.Extension.CanExtendAsync(Id(student), f.AssignmentId, f.CourseId.ToString());

        Assert.That(f.Audit.Entries, Has.Count.EqualTo(1));
        var (request, decision) = f.Audit.Entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(request.Subject!.Id, Is.EqualTo(Id(student)));
            Assert.That(request.Action, Is.EqualTo(AccessActions.AssignmentManage.Id));
            Assert.That(request.Resource!.Type, Is.EqualTo("assignment"));
            Assert.That(request.Resource.Id, Is.EqualTo(f.AssignmentId.ToString("D")));
        });
    }

    [Test]
    [Property("RequirementId", "AUD-01")]
    public async Task AllowedExtension_IsAuditedAsAllow()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);

        await f.Extension.CanExtendAsync(Id(teacher), f.AssignmentId, f.CourseId.ToString());

        Assert.That(f.Audit.Entries, Has.Count.EqualTo(1));
        Assert.That(f.Audit.Entries[0].Decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    // ---- ICourseEnrollmentCheck ----

    [Test]
    [Property("RequirementId", "ACC-02")]
    public async Task IsActiveStudent_WhenActiveStudentOfThatCourse_IsTrue()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student);

        var enrolled = await f.Enrollment.IsActiveStudentAsync(Id(student), f.CourseId.ToString());

        Assert.That(enrolled, Is.True);
    }

    [Test]
    [Property("RequirementId", "ACC-17")]
    public async Task IsActiveStudent_WhenStudentOfAnotherCourse_IsFalse()
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.OtherCourseId, CourseRole.Student);

        var enrolled = await f.Enrollment.IsActiveStudentAsync(Id(student), f.CourseId.ToString());

        Assert.That(enrolled, Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-17")]
    public async Task IsActiveStudent_WhenTeacherOrAssistant_IsFalse()
    {
        var f = new Fixture();
        var teacher = f.AddAccount();
        f.AddMember(teacher, f.CourseId, CourseRole.Teacher);
        var assistant = f.AddAccount();
        f.AddMember(assistant, f.CourseId, CourseRole.Assistant);

        var teacherEnrolled = await f.Enrollment.IsActiveStudentAsync(Id(teacher), f.CourseId.ToString());
        var assistantEnrolled = await f.Enrollment.IsActiveStudentAsync(Id(assistant), f.CourseId.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(teacherEnrolled, Is.False);
            Assert.That(assistantEnrolled, Is.False);
        });
    }

    [TestCase(MembershipStatus.Left)]
    [TestCase(MembershipStatus.Suspended)]
    [Property("RequirementId", "ACC-02")]
    public async Task IsActiveStudent_WhenMembershipIsNotActive_IsFalse(MembershipStatus status)
    {
        var f = new Fixture();
        var student = f.AddAccount();
        f.AddMember(student, f.CourseId, CourseRole.Student, status);

        var enrolled = await f.Enrollment.IsActiveStudentAsync(Id(student), f.CourseId.ToString());

        Assert.That(enrolled, Is.False);
    }

    [TestCase("", "")]
    [TestCase("student-1", "course-oib")]
    [TestCase("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000")]
    [Property("RequirementId", "ACC-17")]
    public async Task IsActiveStudent_WhenIdsAreNotGuids_IsFalse(string studentId, string courseId)
    {
        var f = new Fixture();

        var enrolled = await f.Enrollment.IsActiveStudentAsync(studentId, courseId);

        Assert.That(enrolled, Is.False);
    }

    // ---- helpers ----

    private static string Id(Guid account) => account.ToString("D");

    private sealed class Fixture
    {
        public Guid CourseId { get; } = Guid.NewGuid();
        public Guid OtherCourseId { get; } = Guid.NewGuid();
        public Guid AssignmentId { get; } = Guid.NewGuid();

        public InMemoryMembershipReader Memberships { get; } = new();
        public FakeAccounts Accounts { get; } = new();
        public RecordingAccessAudit Audit { get; } = new();

        public AccessDeadlineExtensionAuthorization Extension { get; }
        public AccessStartAttemptAuthorization StartAttempt { get; }
        public MembershipCourseEnrollmentCheck Enrollment { get; }

        public Fixture()
        {
            var resources = new InMemoryResourceDescriptorResolver();
            resources.SetCourse(new ResourceRef("assignment", AssignmentId.ToString("D")), CourseId);

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
                Audit,
                new AccessDecisionOptions());

            Extension = new AccessDeadlineExtensionAuthorization(engine, Accounts);
            StartAttempt = new AccessStartAttemptAuthorization(engine, Accounts);
            Enrollment = new MembershipCourseEnrollmentCheck(Memberships);
        }

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
}
