using Microsoft.EntityFrameworkCore;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Membership.Tests.Persistence;

// ACC-02-2: pravilo jedinstvenosti na pravoj PostgreSQL bazi (EF in-memory ga ne proverava).
// Test sam podiže privremenu PostgreSQL bazu u Docker-u (Testcontainers, isto kao Identity i
// Laboratories testovi), pa se izvršava i u CI-ju, a ne preskače se. Lokalno treba upaljen Docker.
// Upis ide zajedno sa audit zapisom (AUD-01), pa se koristi pravi audit writer Tima 12.
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class MembershipPostgresTests
{
    private static readonly AuditContext Audit =
        new(AuditActor.ForUser(Guid.Parse("44444444-4444-4444-4444-444444444444")), "acc02-postgres-test");

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("membership_tests")
        .WithUsername("membership_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    private readonly List<Guid> courses = [];
    private TestContextFactory factory = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await database.StartAsync();
        factory = new TestContextFactory(database.GetConnectionString());

        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();

        // Upis i audit su jedna transakcija, pa audit šema mora biti u istoj bazi.
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(database.GetConnectionString());
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        // Privremena baza se briše zajedno sa svim podacima.
        await database.DisposeAsync();
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
        var store = Store();
        var course = NewCourse();
        var student = Guid.NewGuid();

        var first = await store.AddAsync(Student(course, student), Audit);
        var second = await store.AddAsync(Student(course, student), Audit);

        Assert.That(first, Is.EqualTo(AddMemberResult.Added));
        Assert.That(second, Is.EqualTo(AddMemberResult.AlreadyActive));
        Assert.That(await CountAsync(course, student), Is.EqualTo(1));
    }

    [Test]
    public async Task Two_simultaneous_enrollments_create_only_one_membership()
    {
        var store = Store();
        var course = NewCourse();
        var student = Guid.NewGuid();

        var results = await Task.WhenAll(
            store.AddAsync(Student(course, student), Audit).AsTask(),
            store.AddAsync(Student(course, student), Audit).AsTask());

        Assert.That(results.Count(r => r == AddMemberResult.Added), Is.EqualTo(1));
        Assert.That(results.Count(r => r == AddMemberResult.AlreadyActive), Is.EqualTo(1));
        Assert.That(await CountAsync(course, student), Is.EqualTo(1));
    }

    [Test]
    public async Task A_student_who_left_can_enroll_again_and_history_stays()
    {
        var store = Store();
        var course = NewCourse();
        var student = Guid.NewGuid();
        var old = Student(course, student);
        old.Leave(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var oldResult = await store.AddAsync(old, Audit);
        var newResult = await store.AddAsync(Student(course, student), Audit);

        Assert.That(oldResult, Is.EqualTo(AddMemberResult.Added));
        Assert.That(newResult, Is.EqualTo(AddMemberResult.Added));
        Assert.That(await CountAsync(course, student), Is.EqualTo(2));
        Assert.That((await store.FindActiveAsync(course, student))!.Status, Is.EqualTo(MemberStatus.Active));
    }

    private EfMembershipStore Store() =>
        new(factory, new PostgresTransactionalAuditWriter(TimeProvider.System));

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