using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Modules.Assignments.Composition;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Tests.AssignmentsAuditApi;

namespace Platform.Tests.AssignmentsIntegration;

// ASN-04 / ASN-06 TIM 15: integracioni testovi STVARNIH adaptera autorizacije.
// Nema test dvojnika za odluku: koristi se pravi Access modul (AddAccessControl ->
// DefaultDenyAccessDecision + CourseRoleAccessPolicy + katalog radnji). Zamenjeni su samo izvori
// podataka drugih modula: članstvo na kursu (tim 3), status naloga (Identity) i skladište dodela.
[TestFixture]
public sealed class AccessAuthorizationAdapterTests
{
    private static readonly Guid Course = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000015");
    private static readonly Guid OtherCourse = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000016");
    private static readonly Guid Teacher = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Outsider = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Assistant = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BlockedStudent = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherTeacher = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid FormerStudent = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private InMemoryMembershipReader _memberships = null!;
    private InMemoryAssignmentRepository _assignments = null!;
    private Assignment _assignment = null!;

    [SetUp]
    public void SetUp()
    {
        _memberships = new InMemoryMembershipReader();
        _memberships.Set(Member(Teacher, Course, CourseRole.Teacher));
        _memberships.Set(Member(Student, Course, CourseRole.Student));
        _memberships.Set(Member(BlockedStudent, Course, CourseRole.Student));
        _memberships.Set(Member(OtherTeacher, OtherCourse, CourseRole.Teacher));
        _memberships.Set(Member(FormerStudent, Course, CourseRole.Student) with { Status = MembershipStatus.Left });
        _memberships.Set(Member(Assistant, Course, CourseRole.Assistant) with
        {
            DelegatedActions = new HashSet<string>(StringComparer.Ordinal) { "assignment.manage", "attempt.create" }
        });

        _assignments = new InMemoryAssignmentRepository();
        _assignment = NewAssignment(Course.ToString("D"));
        _assignments.Add(_assignment);
    }

    // ---------- ASN-04: IStartAttemptAuthorization -> "attempt.create" ----------

    [Test]
    public async Task StartAttempt_EnrolledActiveStudent_IsAllowed()
    {
        await using var provider = Build();
        Assert.That(await CanStart(provider, Student, _assignment), Is.True);
    }

    [TestCase("33333333-3333-3333-3333-333333333333", TestName = "StartAttempt_Outsider_IsDenied")]
    [TestCase("11111111-1111-1111-1111-111111111111", TestName = "StartAttempt_Teacher_IsDenied_OnlyStudentsStart")]
    [TestCase("44444444-4444-4444-4444-444444444444", TestName = "StartAttempt_Assistant_IsDenied_ActionNotDelegable")]
    [TestCase("55555555-5555-5555-5555-555555555555", TestName = "StartAttempt_BlockedAccount_IsDenied_EvenWithMembership")]
    [TestCase("77777777-7777-7777-7777-777777777777", TestName = "StartAttempt_FormerStudent_IsDenied")]
    [TestCase("99999999-9999-9999-9999-999999999999", TestName = "StartAttempt_UnknownAccount_IsDenied")]
    public async Task StartAttempt_NotAnActiveStudentOfTheCourse_IsDenied(string actor)
    {
        await using var provider = Build();
        Assert.That(await CanStart(provider, Guid.Parse(actor), _assignment), Is.False);
    }

    // ---------- ASN-06: IDeadlineExtensionAuthorization -> "assignment.manage" ----------

    [Test]
    public async Task ExtendDeadline_TeacherOfTheCourse_IsAllowed()
    {
        await using var provider = Build();
        Assert.That(await CanExtend(provider, Teacher.ToString("D"), _assignment), Is.True);
    }

    [TestCase("22222222-2222-2222-2222-222222222222", TestName = "ExtendDeadline_Student_IsDenied")]
    [TestCase("66666666-6666-6666-6666-666666666666", TestName = "ExtendDeadline_TeacherOfAnotherCourse_IsDenied")]
    [TestCase("44444444-4444-4444-4444-444444444444", TestName = "ExtendDeadline_AssistantEvenWithDelegation_IsDenied")]
    [TestCase("33333333-3333-3333-3333-333333333333", TestName = "ExtendDeadline_Outsider_IsDenied")]
    [TestCase("teacher", TestName = "ExtendDeadline_ActorThatIsNotAGuid_IsDenied")]
    [TestCase("11111111-1111-1111-1111-111111111111' OR '1'='1", TestName = "ExtendDeadline_InjectedActor_IsDenied")]
    [TestCase("", TestName = "ExtendDeadline_EmptyActor_IsDenied")]
    public async Task ExtendDeadline_NotTheCourseTeacher_IsDenied(string actor)
    {
        await using var provider = Build();
        Assert.That(await CanExtend(provider, actor, _assignment), Is.False);
    }

    [Test]
    public async Task CourseIsResolvedFromTheStoredAssignment_NotFromTheCallerSuppliedCourse()
    {
        await using var provider = Build();
        using var scope = provider.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IDeadlineExtensionAuthorization>();

        // Nastavnik drugog kursa tvrdi da je dodela na NJEGOVOM kursu: odlučuje kurs iz skladišta.
        var allowed = await authorization.CanExtendAsync(
            OtherTeacher.ToString("D"), _assignment.Id, OtherCourse.ToString("D"));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task UnknownAssignment_IsDenied_ForEveryone()
    {
        await using var provider = Build();
        var unknown = NewAssignment(Course.ToString("D")); // nije u skladištu

        var result0 = await CanStart(provider, Student, unknown);
        var result1 = await CanExtend(provider, Teacher.ToString("D"), unknown);

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.False);
            Assert.That(result1, Is.False);
        });
    }

    [Test]
    public async Task AssignmentWithNonGuidCourse_IsDenied_FailClosed()
    {
        var legacy = NewAssignment("course-oib");
        _assignments.Add(legacy);
        await using var provider = Build();

        var result0 = await CanStart(provider, Student, legacy);
        var result1 = await CanExtend(provider, Teacher.ToString("D"), legacy);

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.False);
            Assert.That(result1, Is.False);
        });
    }

    [Test]
    public async Task RepositoryFailure_IsDenied_AndDoesNotThrow()
    {
        await using var provider = Build(repository: new ThrowingAssignmentRepository());

        var result0 = await CanStart(provider, Student, _assignment);
        var result1 = await CanExtend(provider, Teacher.ToString("D"), _assignment);

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.False);
            Assert.That(result1, Is.False);
        });
    }

    [Test]
    public async Task MembershipSourceFailure_IsDenied_AndDoesNotThrow()
    {
        await using var provider = Build(memberships: new ThrowingMembershipReader());

        var result0 = await CanStart(provider, Student, _assignment);
        var result1 = await CanExtend(provider, Teacher.ToString("D"), _assignment);
        var result2 = await IsEnrolled(provider, Student.ToString("D"), Course.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.False);
            Assert.That(result1, Is.False);
            Assert.That(result2, Is.False);
        });
    }

    [Test]
    public async Task ProductionDefaultMembership_EmptyReader_DeniesEverything()
    {
        // Bez izvora članstva (tim 3) Access modul ne zna ni za jednog člana: sve se odbija (ACC-05).
        await using var provider = Build(useProductionMembershipDefault: true);

        var result0 = await CanStart(provider, Student, _assignment);
        var result1 = await CanExtend(provider, Teacher.ToString("D"), _assignment);
        var result2 = await IsEnrolled(provider, Student.ToString("D"), Course.ToString("D"));

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.False);
            Assert.That(result1, Is.False);
            Assert.That(result2, Is.False);
        });
    }

    // ---------- ASN-06: ICourseEnrollmentCheck ----------

    [Test]
    public async Task Enrollment_ActiveStudent_IsTrue()
    {
        await using var provider = Build();
        Assert.That(await IsEnrolled(provider, Student.ToString("D"), Course.ToString("D")), Is.True);
    }

    [TestCase("11111111-1111-1111-1111-111111111111", "c0c0c0c0-0000-4000-8000-000000000015", TestName = "Enrollment_Teacher_IsFalse")]
    [TestCase("77777777-7777-7777-7777-777777777777", "c0c0c0c0-0000-4000-8000-000000000015", TestName = "Enrollment_FormerStudent_IsFalse")]
    [TestCase("22222222-2222-2222-2222-222222222222", "c0c0c0c0-0000-4000-8000-000000000016", TestName = "Enrollment_StudentOfAnotherCourse_IsFalse")]
    [TestCase("student-1", "c0c0c0c0-0000-4000-8000-000000000015", TestName = "Enrollment_NonGuidStudent_IsFalse")]
    [TestCase("22222222-2222-2222-2222-222222222222", "course-oib", TestName = "Enrollment_NonGuidCourse_IsFalse")]
    public async Task Enrollment_NotAnActiveStudent_IsFalse(string student, string course)
    {
        await using var provider = Build();
        Assert.That(await IsEnrolled(provider, student, course), Is.False);
    }

    // ---------- kompozicija ----------

    [Test]
    public async Task ResourceResolver_DecoratesThePreviousResolver_OtherResourceTypesAreUnchanged()
    {
        var previous = new InMemoryResourceDescriptorResolver();
        var laboratory = new ResourceRef("laboratory", "lab-1");
        previous.SetCourse(laboratory, OtherCourse);

        await using var provider = Build(previousResolver: previous);
        var resolver = provider.GetRequiredService<IResourceDescriptorResolver>();

        var result0 = await resolver.ResolveCourseIdAsync(laboratory);
        var result1 = await resolver.ResolveCourseIdAsync(new ResourceRef("assignment", _assignment.Id.ToString("D")));
        var result2 = await resolver.ResolveCourseIdAsync(new ResourceRef("assignment", "not-a-guid"));

        Assert.Multiple(() =>
        {
            Assert.That(result0, Is.EqualTo(OtherCourse));
            Assert.That(result1, Is.EqualTo(Course));
            Assert.That(result2, Is.Null);
        });
    }

    [Test]
    public void AddAssignmentsAccessAuthorization_CalledTwice_KeepsOneResolverAndValidatesOnBuild()
    {
        var services = BaseServices(_memberships, _assignments, previousResolver: null, useProductionMembershipDefault: false);
        services.AddAssignmentsAccessAuthorization();
        services.AddAssignmentsAccessAuthorization();

        Assert.That(services.Count(d => d.ServiceType == typeof(IResourceDescriptorResolver)), Is.EqualTo(1));
        Assert.DoesNotThrow(() =>
        {
            using var provider = services.BuildServiceProvider(StrictOptions);
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<IStartAttemptAuthorization>();
            scope.ServiceProvider.GetRequiredService<IDeadlineExtensionAuthorization>();
            scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCheck>();
        });
    }

    // ---------- pomoćne metode ----------

    private static ServiceProviderOptions StrictOptions => new() { ValidateScopes = true, ValidateOnBuild = true };

    private ServiceProvider Build(
        IAssignmentRepository? repository = null,
        IMembershipReader? memberships = null,
        IResourceDescriptorResolver? previousResolver = null,
        bool useProductionMembershipDefault = false)
    {
        var services = BaseServices(
            memberships ?? _memberships, repository ?? _assignments, previousResolver, useProductionMembershipDefault);
        services.AddAssignmentsAccessAuthorization();
        return services.BuildServiceProvider(StrictOptions);
    }

    private static ServiceCollection BaseServices(
        IMembershipReader memberships,
        IAssignmentRepository repository,
        IResourceDescriptorResolver? previousResolver,
        bool useProductionMembershipDefault)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (!useProductionMembershipDefault)
        {
            services.AddSingleton(memberships);
        }

        if (previousResolver is not null)
        {
            services.AddSingleton(previousResolver);
        }

        // Pravi Access modul; TryAdd ne prepisuje članstvo i resolver registrovane iznad.
        services.AddAccessControl();

        services.AddSingleton<IAccountStatusReader, TestAccounts>();
        services.AddScoped<ICorrelationContext, TestCorrelation>();
        services.AddSingleton(repository);
        return services;
    }

    private static async Task<bool> CanStart(IServiceProvider provider, Guid actor, Assignment assignment)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStartAttemptAuthorization>()
            .CanStartAsync(actor.ToString("D"), assignment.Id, assignment.CourseId);
    }

    private static async Task<bool> CanExtend(IServiceProvider provider, string actor, Assignment assignment)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDeadlineExtensionAuthorization>()
            .CanExtendAsync(actor, assignment.Id, assignment.CourseId);
    }

    private static async Task<bool> IsEnrolled(IServiceProvider provider, string student, string course)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCheck>()
            .IsActiveStudentAsync(student, course);
    }

    private static CourseMembership Member(Guid account, Guid course, CourseRole role) =>
        new(account, course, role, MembershipStatus.Active, new HashSet<string>(StringComparer.Ordinal));

    private static Assignment NewAssignment(string courseId) =>
        new(Guid.NewGuid(), courseId, "revision-1", null, DateTimeOffset.UtcNow.AddDays(7), maxAttempts: 2);

    private sealed class TestAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            AccountSnapshot? snapshot =
                accountId == BlockedStudent ? new AccountSnapshot(accountId, AccountStatus.Blocked)
                : accountId == Teacher || accountId == Student || accountId == Outsider || accountId == Assistant
                  || accountId == OtherTeacher || accountId == FormerStudent
                    ? new AccountSnapshot(accountId, AccountStatus.Active)
                    : null;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class TestCorrelation : ICorrelationContext
    {
        public string? CorrelationId => "assignments-access-test";
    }

    private sealed class ThrowingAssignmentRepository : IAssignmentRepository
    {
        public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("db host=10.0.0.5 password=hunter2");

        public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
            IReadOnlyCollection<string> courseIds, CancellationToken cancellationToken = default) =>
            throw new TimeoutException();

        public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default) =>
            throw new TimeoutException();

        public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default) =>
            throw new TimeoutException();
    }

    private sealed class ThrowingMembershipReader : IMembershipReader
    {
        public ValueTask<CourseMembership?> GetAsync(
            Guid accountId, Guid courseId, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("membership store down");
    }
}
