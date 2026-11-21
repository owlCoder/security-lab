using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// IDN-08 acceptance criterion against a real PostgreSQL: a repeated external identity
/// maps to one internal account, without duplicates.
/// </summary>
[TestFixture]
[Category("IDN-08")]
public class ExternalIdentityLinkerPostgresTests
{
    private const string Issuer = "https://idp.example.test/realms/security-lab";
    private const string OtherIssuer = "https://other-idp.example.test";
    private const string Subject = "3f6c0a52-7d1e-4b8a-9c2f-000000000001";
    private const string StudentEmail = "student@example.com";
    private const string TeacherEmail = "teacher@example.com";

    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeacherId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private MovableClock _clock = null!;

    [SetUp]
    public async Task SetUp()
    {
        await TestDatabaseFixture.ResetDatabaseAsync();
        _clock = new MovableClock();

        await using var db = NewContext();
        db.Accounts.Add(new UserAccount(StudentId, StudentEmail, AccountStatus.Active, AccountRole.Student));
        db.Accounts.Add(new UserAccount(TeacherId, TeacherEmail, AccountStatus.Active, AccountRole.Teacher));
        await db.SaveChangesAsync();
    }

    // ---------- Acceptance criterion: repeated identity -> one account, no duplicates ----------

    [Test]
    public async Task FirstLogin_WithVerifiedEmail_LinksTheExistingAccount()
    {
        var result = await ResolveAsync(Verified());

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.NewlyLinked));
        Assert.That(result.AccountId, Is.EqualTo(StudentId));

        var link = (await LinksAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(link.AccountId, Is.EqualTo(StudentId));
            Assert.That(link.Issuer, Is.EqualTo(Issuer));
            Assert.That(link.Subject, Is.EqualTo(Subject));
        });
    }

    [Test]
    public async Task RepeatedLogin_ReturnsTheSameAccount_WithoutASecondLink()
    {
        await ResolveAsync(Verified());
        _clock.Advance(TimeSpan.FromHours(1));

        var second = await ResolveAsync(Verified());
        var third = await ResolveAsync(Verified());

        Assert.Multiple(() =>
        {
            Assert.That(second.Outcome, Is.EqualTo(FederatedLoginOutcome.Linked));
            Assert.That(second.AccountId, Is.EqualTo(StudentId));
            Assert.That(third.AccountId, Is.EqualTo(StudentId));
        });

        var link = (await LinksAsync()).Single();
        Assert.That(link.LastLoginAt, Is.EqualTo(_clock.GetUtcNow()));
    }

    [Test]
    public async Task RepeatedLogin_AfterTheEmailChangedAtTheProvider_StillMapsToTheSameAccount()
    {
        await ResolveAsync(Verified());

        // The provider now reports another (even unverified) e-mail: only (issuer, subject) decides.
        var result = await ResolveAsync(new ExternalIdentity(Issuer, Subject, TeacherEmail, EmailVerified: false));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.Linked));
        Assert.That(result.AccountId, Is.EqualTo(StudentId));
        Assert.That(await LinksAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentFirstLogins_OfTheSameIdentity_CreateExactlyOneLink()
    {
        const int parallel = 10;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, parallel)
            .Select(async _ =>
            {
                await gate.Task;                 // all requests start at the same moment
                return await ResolveAsync(Verified());
            })
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Outcome),
                Has.All.AnyOf(FederatedLoginOutcome.NewlyLinked, FederatedLoginOutcome.Linked));
            Assert.That(results.Select(r => r.AccountId), Has.All.EqualTo(StudentId));
            Assert.That(results.Count(r => r.Outcome == FederatedLoginOutcome.NewlyLinked), Is.EqualTo(1));
        });
        Assert.That(await LinksAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Database_RejectsADuplicateIssuerSubject_EvenWithoutTheService()
    {
        await using (var db = NewContext())
        {
            db.ExternalLogins.Add(new ExternalLogin(Guid.NewGuid(), StudentId, Issuer, Subject, _clock.GetUtcNow()));
            await db.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.ExternalLogins.Add(new ExternalLogin(Guid.NewGuid(), TeacherId, Issuer, Subject, _clock.GetUtcNow()));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = ex!.InnerException as PostgresException;
        Assert.That(pg?.ConstraintName, Is.EqualTo(ExternalIdentityLinker.IssuerSubjectIndex));
    }

    // ---------- First-login rules: no account takeover, no automatic accounts ----------

    [Test]
    public async Task FirstLogin_WithUnverifiedEmail_IsRefused_AndNothingIsStored()
    {
        // Someone registers teacher@example.com at the provider without proving it.
        var result = await ResolveAsync(new ExternalIdentity(Issuer, Subject, TeacherEmail, EmailVerified: false));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.EmailNotVerified));
        Assert.That(result.AccountId, Is.Null);
        Assert.That(await LinksAsync(), Is.Empty);
    }

    [Test]
    public async Task FirstLogin_WithoutEmail_IsRefused()
    {
        var result = await ResolveAsync(new ExternalIdentity(Issuer, Subject, null, EmailVerified: true));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.EmailNotVerified));
        Assert.That(await LinksAsync(), Is.Empty);
    }

    [Test]
    public async Task FirstLogin_WithUnknownEmail_IsRefused_AndNoAccountIsCreated()
    {
        var result = await ResolveAsync(Verified(email: "stranger@example.com"));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.NoMatchingAccount));
        Assert.That(await LinksAsync(), Is.Empty);

        await using var db = NewContext();
        Assert.That(await db.Accounts.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task FirstLogin_NormalizesTheEmail_LikePasswordLogin()
    {
        var result = await ResolveAsync(Verified(email: "  Student@Example.COM "));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.NewlyLinked));
        Assert.That(result.AccountId, Is.EqualTo(StudentId));
    }

    [Test]
    public async Task SecondIdentity_FromTheSameIssuer_ForAnAlreadyLinkedAccount_IsRefused()
    {
        await ResolveAsync(Verified());

        var result = await ResolveAsync(Verified(subject: "another-subject"));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.AccountAlreadyLinked));
        Assert.That(await LinksAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SameSubject_FromAnotherIssuer_IsADifferentIdentity()
    {
        await ResolveAsync(Verified());

        var result = await ResolveAsync(Verified(issuer: OtherIssuer));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.NewlyLinked));
        var links = await LinksAsync();
        Assert.That(links, Has.Count.EqualTo(2));
        Assert.That(links.Select(l => l.AccountId), Has.All.EqualTo(StudentId));
    }

    // ---------- Invalid input ----------

    [TestCase("", Subject)]
    [TestCase("   ", Subject)]
    [TestCase(Issuer, "")]
    [TestCase(Issuer, "   ")]
    public async Task MissingIssuerOrSubject_IsInvalid(string issuer, string subject)
    {
        var result = await ResolveAsync(Verified(issuer: issuer, subject: subject));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.InvalidIdentity));
        Assert.That(await LinksAsync(), Is.Empty);
    }

    [Test]
    public async Task TooLongSubject_IsInvalid()
    {
        var subject = new string('s', ExternalLogin.MaxSubjectLength + 1);

        var result = await ResolveAsync(Verified(subject: subject));

        Assert.That(result.Outcome, Is.EqualTo(FederatedLoginOutcome.InvalidIdentity));
    }

    // ---------- Helpers ----------

    private static PlatformIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseNpgsql(TestDatabaseFixture.ConnectionString)
            .Options);

    // Every call gets its own DbContext, like a separate HTTP request.
    private async Task<FederatedLoginResult> ResolveAsync(ExternalIdentity identity)
    {
        await using var db = NewContext();
        return await new ExternalIdentityLinker(db, _clock).ResolveAsync(identity);
    }

    private static ExternalIdentity Verified(
        string email = StudentEmail, string subject = Subject, string issuer = Issuer) =>
        new(issuer, subject, email, EmailVerified: true);

    private static async Task<List<ExternalLogin>> LinksAsync()
    {
        await using var db = NewContext();
        return await db.ExternalLogins.AsNoTracking().ToListAsync();
    }

    private sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}