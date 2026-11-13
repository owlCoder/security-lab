
using Microsoft.EntityFrameworkCore;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.Persistence;

namespace Platform.Membership.Tests.Persistence;

// ACC-02-2: pravilo jedinstvenosti na pravoj PostgreSQL bazi (EF in-memory ga ne proverava).
// Pokreće se samo kada postoji ConnectionStrings__Membership, inače se preskače (kao kod Tima 20).
[TestFixture]
[Category("Integration")]
public sealed class MembershipPostgresTests
{
    private const string ConnectionVariable = "ConnectionStrings__Membership";

    private readonly List<Guid> courses = [];
    private TestContextFactory factory = null!;

    [OneTimeSetUp]
    public async Task MigrateDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore(
                $"PostgreSQL is not configured ({ConnectionVariable}). Skipping integration tests.");
        }

        factory = new TestContextFactory(connectionString!);

        await using var db = factory.CreateDbContext();
        if (!db.Database.GetMigrations().Any())
        {
            Assert.Ignore("No migrations yet. Run 'dotnet ef migrations add' for MembershipDbContext first.");
        }

        await db.Database.MigrateAsync();
    }

    [TearDown]
    public async Task RemoveTestRows()
    {
        if (courses.Count == 0)
        {
            return;
        }

        await using var db = factory.CreateDbContext();
        await db.CourseMembers.Where(m => courses.Contains(m.CourseId)).ExecuteDeleteAsync();
        courses.Clear();
    }

    [Test]
    public async Task Second_active_membership_is_rejected_by_the_database()
    {
        var store = new EfMembershipStore(factory);
        var course = NewCourse();
        var student = Guid.NewGuid();

        var first = await store.AddAsync(Student(course, student));
        var second = await store.AddAsync(Student(course, student));

        Assert.That(first, Is.EqualTo(AddMemberResult.Added));
        Assert.That(second, Is.EqualTo(AddMemberResult.AlreadyActive));
        Assert.That(await CountAsync(course, student), Is.EqualTo(1));
    }

    [Test]
    public async Task Two_simultaneous_enrollments_create_only_one_membership()
    {
        var store = new EfMembershipStore(factory);
        var course = NewCourse();
        var student = Guid.NewGuid();

        var results = await Task.WhenAll(
            store.AddAsync(Student(course, student)).AsTask(),
            store.AddAsync(Student(course, student)).AsTask());

        Assert.That(results.Count(r => r == AddMemberResult.Added), Is.EqualTo(1));
        Assert.That(results.Count(r => r == AddMemberResult.AlreadyActive), Is.EqualTo(1));
        Assert.That(await CountAsync(course, student), Is.EqualTo(1));
    }

    [Test]
    public async Task A_student_who_left_can_enroll_again_and_history_stays()
    {
        var store = new EfMembershipStore(factory);
        var course = NewCourse();
        var student = Guid.NewGuid();
        var old = Student(course, student);
        old.Leave(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var oldResult = await store.AddAsync(old);
        var newResult = await store.AddAsync(Student(course, student));

        Assert.That(oldResult, Is.EqualTo(AddMemberResult.Added));
        Assert.That(newResult, Is.EqualTo(AddMemberResult.Added));
        Assert.That(await CountAsync(course, student), Is.EqualTo(2));
        Assert.That((await store.FindActiveAsync(course, student))!.Status, Is.EqualTo(MemberStatus.Active));
    }

    private Guid NewCourse()
    {
        var course = Guid.NewGuid();
        courses.Add(course);
        return course;
    }

    private static CourseMember Student(Guid course, Guid student) =>
        CourseMember.Enroll(course, student, MemberRole.Student, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private async Task<int> CountAsync(Guid course, Guid student)
    {
        await using var db = factory.CreateDbContext();
        return await db.CourseMembers.CountAsync(m => m.CourseId == course && m.AccountId == student);
    }

    private sealed class TestContextFactory(string connectionString) : IDbContextFactory<MembershipDbContext>
    {
        public MembershipDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<MembershipDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MembershipDbContext.Schema))
                .Options);
    }
}
